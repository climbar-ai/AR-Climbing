using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;
using Microsoft.MixedReality.Toolkit.UI;
using System.Collections;

[RequireComponent(typeof(SpatialAnchorManager))]
public class AzureSpatialAnchorsScript : MonoBehaviour
{
    /// <summary>
    /// Used to distinguish short taps and long taps
    /// </summary>
    private float[] _tappingTimer = { 0, 0 };

    /// <summary>
    /// Main interface to anything Spatial Anchors related
    /// </summary>
    private SpatialAnchorManager _spatialAnchorManager = null;

    /// <summary>
    /// Used to keep track of all GameObjects that represent a found or created anchor
    /// </summary>
    private List<GameObject> _foundOrCreatedAnchorGameObjects = new List<GameObject>();

    /// <summary>
    /// Used to keep track of all the created Anchor IDs
    /// </summary>
    private List<String> _createdAnchorIDs = new List<String>();

    /// <summary>
    /// Editing modes
    /// </summary>
    private enum EditingMode
    {
        Move,
        Delete
    }

    /// <summary>
    /// Used to track hold editing mode, either delete or move
    /// </summary>
    private EditingMode editingMode;

    /// <summary>
    /// editor mode button
    /// </summary>
    //public Button editorModeButton;

    /// <summary>
    /// hold prefab
    /// </summary>
    public GameObject hold;

    /// <summary>
    /// hold hover script game object
    /// </summary>
    public GameObject holdHoverScript;

    // <Start>
    // Start is called before the first frame update
    void Start()
    {
        _spatialAnchorManager = GetComponent<SpatialAnchorManager>();
        _spatialAnchorManager.LogDebug += (sender, args) => Debug.Log($"ASA - Debug: {args.Message}");
        _spatialAnchorManager.Error += (sender, args) => Debug.LogError($"ASA - Error: {args.ErrorMessage}");
        _spatialAnchorManager.AnchorLocated += SpatialAnchorManager_AnchorLocated;
        editingMode = EditingMode.Move;
    }
    // </Start>

    // <Update>
    // Update is called once per frame
    void Update()
    {

        //Check for any air taps from either hand
        for (int i = 0; i < 2; i++)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode((i == 0) ? XRNode.RightHand : XRNode.LeftHand);
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool isTapping))
            {
                if (!isTapping)
                {
                    //Stopped Tapping or wasn't tapping
                    if (0f < _tappingTimer[i] && _tappingTimer[i] < 1f)
                    {
                        //User has been tapping for less than 1 sec. Get hand position and call ShortTap
                        //if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 handPosition))
                        //{
                        //    ShortTap(handPosition);
                        //}

                        foreach (var source in CoreServices.InputSystem.DetectedInputSources)
                        {
                            // Ignore anything that is not a hand because we want articulated hands
                            if (source.SourceType == Microsoft.MixedReality.Toolkit.Input.InputSourceType.Hand)
                            {
                                foreach (var p in source.Pointers)
                                {
                                    if (p is IMixedRealityNearPointer)
                                    {
                                        // Ignore near pointers, we only want the rays
                                        continue;
                                    }
                                    if (p.Result != null)
                                    {
                                        var startPoint = p.Position;
                                        var endPoint = p.Result.Details.Point;
                                        var hitObject = p.Result.Details.Object;

                                        //if (hitObject)
                                        //{
                                        //    Debug.Log("hit");
                                        //    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                                        //    sphere.transform.localScale = Vector3.one * 0.01f;
                                        //    sphere.transform.position = endPoint;
                                        //}

                                        ShortTap(endPoint);
                                    }

                                }
                            }
                        }
                    }
                    _tappingTimer[i] = 0;
                }
                else
                {
                    _tappingTimer[i] += Time.deltaTime;
                    if (_tappingTimer[i] >= 2f)
                    {
                        //User has been air tapping for at least 2sec. Get hand position and call LongTap
                        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 handPosition))
                        {
                            LongTap();
                        }
                        _tappingTimer[i] = -float.MaxValue; // reset the timer, to avoid retriggering if user is still holding tap
                    }
                }
            }

        }
    }
    // </Update>


    // <ShortTap>
    /// <summary>
    /// Called when a user is air tapping for a short time 
    /// </summary>
    /// <param name="handPosition">Location where tap was registered</param>
    private async void ShortTap(Vector3 handPosition)
    {
        await _spatialAnchorManager.StartSessionAsync();
        if (!IsAnchorNearby(handPosition, out GameObject anchorGameObject))
        {
            //No Anchor Nearby, start session and create an anchor
            await CreateAnchor(handPosition);
        }
        else
        {
            if (editingMode == EditingMode.Delete)
            {
                //Delete nearby Anchor
                DeleteAnchor(anchorGameObject);
            }
        }
    }
    // </ShortTap>

    // <LongTap>
    /// <summary>
    /// Called when a user is air tapping for a long time (>=2 sec)
    /// </summary>
    private async void LongTap()
    {
        if (editingMode == EditingMode.Delete)
        {
            if (_spatialAnchorManager.IsSessionStarted)
            {
                // Stop Session and remove all GameObjects. This does not delete the Anchors in the cloud
                _spatialAnchorManager.DestroySession();
                RemoveAllAnchorGameObjects();
                Debug.Log("ASA - Stopped Session and removed all Anchor Objects");
            }
            else
            {
                //Start session and search for all Anchors previously created
                await _spatialAnchorManager.StartSessionAsync();
                LocateAnchor();
            }
        }
    }
    // </LongTap>

    // <RemoveAllAnchorGameObjects>
    /// <summary>
    /// Destroys all Anchor GameObjects
    /// </summary>
    private void RemoveAllAnchorGameObjects()
    {
        foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
        {
            Destroy(anchorGameObject);
        }
        _foundOrCreatedAnchorGameObjects.Clear();
    }
    // </RemoveAllAnchorGameObjects>

    // <IsAnchorNearby>
    /// <summary>
    /// Returns true if an Anchor GameObject is within 15cm of the received reference position
    /// </summary>
    /// <param name="position">Reference position</param>
    /// <param name="anchorGameObject">Anchor GameObject within 15cm of received position. Not necessarily the nearest to this position. If no AnchorObject is within 15cm, this value will be null</param>
    /// <returns>True if a Anchor GameObject is within 15cm</returns>
    private bool IsAnchorNearby(Vector3 position, out GameObject anchorGameObject)
    {
        anchorGameObject = null;

        if (_foundOrCreatedAnchorGameObjects.Count <= 0)
        {
            return false;
        }

        //Iterate over existing anchor gameobjects to find the nearest
        var (distance, closestObject) = _foundOrCreatedAnchorGameObjects.Aggregate(
            new Tuple<float, GameObject>(Mathf.Infinity, null),
            (minPair, gameobject) =>
            {
                Vector3 gameObjectPosition = gameobject.transform.position;
                float distance = (position - gameObjectPosition).magnitude;
                return distance < minPair.Item1 ? new Tuple<float, GameObject>(distance, gameobject) : minPair;
            });

        if (distance <= 0.15f)
        {
            //Found an anchor within 15cm
            anchorGameObject = closestObject;
            return true;
        }
        else
        {
            return false;
        }
    }
    // </IsAnchorNearby>

    /// <summary>
    /// OnHoverStarted handler for hold GameObject.
    /// Changes the color.
    /// </summary>
    /// <param name="eventdata"></param>
    /// <param name="hold"></param>
    private void holdOnHoverStartedHandler(ManipulationEventData eventdata, GameObject hold)
    {
        MeshRenderer m_Renderer = hold.GetComponent<MeshRenderer>();
        m_Renderer.material.color = Color.red;
    }

    /// <summary>
    /// OnHoverExited handler for hold GameObjects.
    /// Changes the color.
    /// </summary>
    /// <param name="eventdata"></param>
    /// <param name="hold"></param>
    private void holdOnHoverExitedHandler(ManipulationEventData eventdata, GameObject hold)
    {
        MeshRenderer m_Renderer = hold.GetComponent<MeshRenderer>();
        m_Renderer.material.color = Color.green;
    }

    private void hello4(ManipulationEventData edata)
    {
        Debug.Log("Hello4");
        Debug.Log(edata);
        Debug.Log(this);
    }

    // <CreateAnchor>
    /// <summary>
    /// Creates an Azure Spatial Anchor at the given position rotated towards the user
    /// </summary>
    /// <param name="position">Position where Azure Spatial Anchor will be created</param>
    /// <returns>Async Task</returns>
    private async Task CreateAnchor(Vector3 position)
    {
        //Create Anchor GameObject. We will use ASA to save the position and the rotation of this GameObject.
        if (!InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition))
        {
            headPosition = Vector3.zero;
        }

        Quaternion orientationTowardsHead = Quaternion.LookRotation(position - headPosition, Vector3.up);

        //GameObject anchorGameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //GameObject hoverScript = Instantiate(holdHoverScript);
        GameObject anchorGameObject = Instantiate(hold);
        //hoverScript.GetComponent<onHoldHover>().m_GameObject = anchorGameObject;
        //anchorGameObject.GetComponent<Interactable>().OnFocusEnter;
        Debug.Log("here");
        anchorGameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
        anchorGameObject.transform.position = position;
        anchorGameObject.transform.rotation = orientationTowardsHead;
        anchorGameObject.transform.localScale = Vector3.one * 0.1f;

        //Add and configure ASA components
        CloudNativeAnchor cloudNativeAnchor = anchorGameObject.AddComponent<CloudNativeAnchor>();
        await cloudNativeAnchor.NativeToCloud();
        CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;
        cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(3);

        //Collect Environment Data
        while (!_spatialAnchorManager.IsReadyForCreate)
        {
            float createProgress = _spatialAnchorManager.SessionStatus.RecommendedForCreateProgress;
            Debug.Log($"ASA - Move your device to capture more environment data: {createProgress:0%}");
        }

        Debug.Log($"ASA - Saving cloud anchor... ");

        try
        {
            // Now that the cloud spatial anchor has been prepared, we can try the actual save here.
            await _spatialAnchorManager.CreateAnchorAsync(cloudSpatialAnchor);

            bool saveSucceeded = cloudSpatialAnchor != null;
            if (!saveSucceeded)
            {
                Debug.LogError("ASA - Failed to save, but no exception was thrown.");
                return;
            }

            Debug.Log($"ASA - Saved cloud anchor with ID: {cloudSpatialAnchor.Identifier}");
            _foundOrCreatedAnchorGameObjects.Add(anchorGameObject);
            _createdAnchorIDs.Add(cloudSpatialAnchor.Identifier);
            anchorGameObject.GetComponent<MeshRenderer>().material.color = Color.green;

            // add manipulation scripts: https://stackoverflow.com/questions/61663652/adding-manipulation-components-via-c-sharp-script-only-works-in-unity
            Debug.Log($"Adding manipulation");
            Mesh mesh = anchorGameObject.GetComponent<MeshFilter>().mesh;

            //Add MeshCollider
            MeshCollider collider = anchorGameObject.EnsureComponent<MeshCollider>();

            //A lot of components are curved and need convex set to false
            collider.convex = true;

            //Add NearInteractionGrabbable
            anchorGameObject.EnsureComponent<NearInteractionGrabbable>();

            //Add ManipulationHandler
            var handler = anchorGameObject.EnsureComponent<ObjectManipulator>();
            handler.OnHoverEntered.AddListener((eventData) => holdOnHoverStartedHandler(eventData, anchorGameObject));
            handler.OnHoverExited.AddListener((eventData) => holdOnHoverExitedHandler(eventData, anchorGameObject));
            //handler.OnManipulationEnded.AddListener(hello3);

            //Set mesh to MeshCollider
            collider.sharedMesh = mesh;

            // Disable maninpulation scripts if we are in 'Delete' mode
            if (editingMode == EditingMode.Delete)
            {
                anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
                anchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
            }
        }
        catch (Exception exception)
        {
            Debug.Log("ASA - Failed to save anchor: " + exception.ToString());
            Debug.LogException(exception);
        }
    }
    // </CreateAnchor>

    // <LocateAnchor>
    /// <summary>
    /// Looking for anchors with ID in _createdAnchorIDs
    /// </summary>
    private void LocateAnchor()
    {
        if (_createdAnchorIDs.Count > 0)
        {
            //Create watcher to look for all stored anchor IDs
            Debug.Log($"ASA - Creating watcher to look for {_createdAnchorIDs.Count} spatial anchors");
            AnchorLocateCriteria anchorLocateCriteria = new AnchorLocateCriteria();
            anchorLocateCriteria.Identifiers = _createdAnchorIDs.ToArray();
            _spatialAnchorManager.Session.CreateWatcher(anchorLocateCriteria);
            Debug.Log($"ASA - Watcher created!");
        }
    }
    // </LocateAnchor>

    // <SpatialAnchorManagerAnchorLocated>
    /// <summary>
    /// Callback when an anchor is located
    /// </summary>
    /// <param name="sender">Callback sender</param>
    /// <param name="args">Callback AnchorLocatedEventArgs</param>
    private void SpatialAnchorManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
    {
        Debug.Log($"ASA - Anchor recognized as a possible anchor {args.Identifier} {args.Status}");

        if (args.Status == LocateAnchorStatus.Located)
        {
            //Creating and adjusting GameObjects have to run on the main thread. We are using the UnityDispatcher to make sure this happens.
            UnityDispatcher.InvokeOnAppThread(() =>
            {
                // Read out Cloud Anchor values
                CloudSpatialAnchor cloudSpatialAnchor = args.Anchor;

                //Create GameObject
                //GameObject anchorGameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                GameObject anchorGameObject = Instantiate(hold);
                anchorGameObject.transform.localScale = Vector3.one * 0.1f;
                anchorGameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
                anchorGameObject.GetComponent<MeshRenderer>().material.color = Color.blue;

                // change color to green after a delay
                StartCoroutine(changeColor(anchorGameObject, Color.green, 2f));

                // Link to Cloud Anchor
                anchorGameObject.AddComponent<CloudNativeAnchor>().CloudToNative(cloudSpatialAnchor);
                _foundOrCreatedAnchorGameObjects.Add(anchorGameObject);

                // Add and then disable maninpulation scripts since we are in 'Delete' mode
                Debug.Log($"Adding manipulation");
                Mesh mesh = anchorGameObject.GetComponent<MeshFilter>().mesh;

                //Add MeshCollider
                MeshCollider collider = anchorGameObject.EnsureComponent<MeshCollider>();

                //A lot of components are curved and need convex set to false
                collider.convex = true;

                //Add NearInteractionGrabbable
                anchorGameObject.EnsureComponent<NearInteractionGrabbable>();

                //Add ManipulationHandler
                var handler = anchorGameObject.EnsureComponent<ObjectManipulator>();
                handler.OnHoverEntered.AddListener((eventData) => holdOnHoverStartedHandler(eventData, anchorGameObject));
                handler.OnHoverExited.AddListener((eventData) => holdOnHoverExitedHandler(eventData, anchorGameObject));
                //handler.OnManipulationEnded.AddListener(hello3);

                //Set mesh to MeshCollider
                collider.sharedMesh = mesh;

                // Disable maninpulation scripts since we are in 'Delete' mode
                anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
                anchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
            });
        }
    }
    // </SpatialAnchorManagerAnchorLocated>

    // <DeleteAnchor>
    /// <summary>
    /// Deleting Cloud Anchor attached to the given GameObject and deleting the GameObject
    /// </summary>
    /// <param name="anchorGameObject">Anchor GameObject that is to be deleted</param>
    private async void DeleteAnchor(GameObject anchorGameObject)
    {
        CloudNativeAnchor cloudNativeAnchor = anchorGameObject.GetComponent<CloudNativeAnchor>();
        CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;

        Debug.Log($"ASA - Deleting cloud anchor: {cloudSpatialAnchor.Identifier}");

        //Request Deletion of Cloud Anchor
        await _spatialAnchorManager.DeleteAnchorAsync(cloudSpatialAnchor);

        //Remove local references
        _createdAnchorIDs.Remove(cloudSpatialAnchor.Identifier);
        _foundOrCreatedAnchorGameObjects.Remove(anchorGameObject);
        Destroy(anchorGameObject);

        Debug.Log($"ASA - Cloud anchor deleted!");
    }
    // </DeleteAnchor>

    public void ToggleEditingMode()
    {
        Debug.Log(editingMode);

        if (editingMode == EditingMode.Move)
        {
            editingMode = EditingMode.Delete;

            // make holds unmovable
            foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
            {
                Debug.Log("Disabling Manipulation");
                // disable manipulation scripts if attached
                if (anchorGameObject.GetComponent<NearInteractionGrabbable>() != null)
                {
                    anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
                }
                if (anchorGameObject.GetComponent<ObjectManipulator>() != null)
                {
                    anchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
                }
            }
        } else if (editingMode == EditingMode.Delete)
        {
            editingMode = EditingMode.Move;

            // make holds movable
            foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
            {
                // enable manipulation scripts
                Debug.Log("Enabling Manipulation");
                anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = true;
                anchorGameObject.GetComponent<ObjectManipulator>().enabled = true;
            }
        }
    }

    /// <summary>
    /// Change color of game object.
    /// Used in delayed fashion.
    /// </summary>
    /// <param name="gameObject"></param>
    private IEnumerator changeColor(GameObject gameObject, Color color, float time)
    { 
        yield return new WaitForSeconds(time);
        gameObject.GetComponent<MeshRenderer>().material.color = color;
    }
}
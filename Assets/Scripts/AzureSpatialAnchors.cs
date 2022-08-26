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
using Microsoft.MixedReality.Toolkit.UI;
using System.Collections;
using TMPro;
using Photon.Pun;
using Microsoft.MixedReality.Toolkit.Utilities.Solvers;
using MultiUserCapabilities;

namespace AzureSpatialAnchors
{
    [RequireComponent(typeof(SpatialAnchorManager))]
    public class AzureSpatialAnchors : MonoBehaviourPunCallbacks
    {
        //[SerializeField]
        //private GameObject player;

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
        private GameObject[] _foundOrCreatedAnchorGameObjects;

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
        private string hold;

        /// <summary>
        /// hold hover script game object
        /// </summary>
        //public GameObject holdHoverScript;

        /// <summary>
        /// sphere prefab for long tap user feedback
        /// </summary>
        public GameObject longTapSphere;

        /// <summary>
        /// Progress indicator object
        /// </summary>
        [SerializeField]
        private GameObject indicatorObject;

        /// <summary>
        /// Signals when user has finished placing object and surface magnetism should turn off for that object
        /// NOTE: doesn't work
        /// </summary>
        //private UnityEvent stoppedPlacement;

        private GameObject ghost = default;

        TaskCompletionSource<CloudSpatialAnchor> taskWaitForAnchorLocation;

        // <Start>
        // Start is called before the first frame update
        void Start()
        {
            //_spatialAnchorManager = GetComponent<SpatialAnchorManager>();
            //_spatialAnchorManager.LogDebug += (sender, args) => Debug.Log($"ASA - Debug: {args.Message}");
            //_spatialAnchorManager.Error += (sender, args) => Debug.LogError($"ASA - Error: {args.ErrorMessage}");
            //_spatialAnchorManager.AnchorLocated += SpatialAnchorManager_AnchorLocated;
            editingMode = EditingMode.Move;
            GameObject.Find("ToggleEditorMode").GetComponentInChildren<TextMeshPro>().text = "Mode: Move";
            indicatorObject.SetActive(false);
            hold = "Hold_1_Simple";

            // grab list of holds in case we have joined the room after other clients have created holds
//#if UNITY_2020
//            ExitGames.Client.Photon.Hashtable customRoomProperties = (ExitGames.Client.Photon.Hashtable)PhotonNetwork.CurrentRoom.CustomProperties;
//            if (customRoomProperties.ContainsKey("holds"))
//            {
//                _foundOrCreatedAnchorGameObjects = (List<GameObject>)customRoomProperties["holds"];
//            }
//#endif
            //stoppedPlacement.AddListener(HoldOnPlacingStopped);
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
                            //User has been tapping for less than 1 sec. Get hand-ray's end position and call ShortTap
                            foreach (var source in CoreServices.InputSystem.DetectedInputSources)
                            {
                                // Ignore anything that is not a hand because we want articulated hands
                                if (source.SourceType == Microsoft.MixedReality.Toolkit.Input.InputSourceType.Hand)
                                {
                                    foreach (var p in source.Pointers)
                                    {
                                        if (p is IMixedRealityNearPointer && editingMode != EditingMode.Delete) // we want to be able to use direct touch to delete game objects
                                        {
                                            // Ignore near pointers, we only want the rays
                                            Debug.Log("Near Pointer");
                                            continue;
                                        }
                                        if (p.Result != null)
                                        {
                                            var startPoint = p.Position;
                                            var endPoint = p.Result.Details.Point;
                                            var hitObject = p.Result.Details.Object;

                                            // we need the surface normal of the spatial mesh we want to place the hold on
                                            LayerMask mask = LayerMask.GetMask("Spatial Awareness");
                                            if (Physics.Raycast(startPoint, endPoint - startPoint, out var hit, Mathf.Infinity, mask)) // check if successful before calling ShortTap
                                            {
                                                Debug.Log($"Hit layer: {hit.collider.gameObject.layer}");
                                                PhotonView photonView = PhotonView.Get(this);
                                                ShortTap(hit.point, hit.normal, photonView);
                                            }
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
                            if (editingMode == EditingMode.Delete)
                            {
                                //User has been air tapping for at least 2sec. Get hand position and call LongTap
                                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 handPosition))
                                {
                                    // feedback for user on long airtap by briefly displaying a dot at the cursor
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

                                                    //Quaternion orientationTowardsHead = Quaternion.LookRotation(handPosition - headPosition, Vector3.up);
                                                    //GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                                                    GameObject gameObject = PhotonNetwork.Instantiate(longTapSphere.name, endPoint, Quaternion.identity);
                                                    //gameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
                                                    gameObject.transform.position = endPoint;
                                                    gameObject.transform.rotation = Quaternion.identity;
                                                    gameObject.transform.localScale = Vector3.one * 0.05f;
                                                    //gameObject.GetComponent<MeshRenderer>().material.color = Color.blue;

                                                    StartCoroutine(DestroyObjectDelayed(gameObject, .2f));
                                                }
                                            }
                                        }
                                    }
                                }

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
        /// Called when a user is air tapping for a short time.
        /// We pass the PhotonView in since this is an async function and loses context
        /// </summary>
        /// <param name="handPosition">Location where tap was registered</param>
        private async void ShortTap(Vector3 handPosition, Vector3 surfaceNormal, PhotonView photonView)
        {
            bool anchorNearby = IsAnchorNearby(handPosition, out GameObject anchorGameObject);

            if (!anchorNearby && editingMode == EditingMode.Move)
            {
                // No Anchor Nearby, start session and create an anchor
                CreateAnchor(handPosition, surfaceNormal, photonView);
            }
            else if (anchorNearby && editingMode == EditingMode.Move)
            {
                // Toggle TapToPlace on so we can start or end moving the object
                //anchorGameObject.GetComponent<TapToPlace>().enabled = !anchorGameObject.GetComponent<TapToPlace>().enabled;
                bool isTappingToPlace = anchorGameObject.GetComponent<HoldData>().isTappingToPlace;
                anchorGameObject.GetComponent<HoldData>().isTappingToPlace = !anchorGameObject.GetComponent<HoldData>().isTappingToPlace;
                //TapToPlace ttp = anchorGameObject.GetComponent<TapToPlace>();
                SurfaceMagnetism sm = anchorGameObject.EnsureComponent<SurfaceMagnetism>();
                if (isTappingToPlace)
                {
                    //ttp.StopPlacement();
                    sm.enabled = false;

                    // make visible and hide ghost hold
                    PhotonNetwork.Destroy(ghost);
                    //anchorGameObject.GetComponent<MeshRenderer>().enabled = true;
                    anchorGameObject.GetComponent<OnHoldMove>().OnMoveExit();
                    await HoldOnPlacingStopped(anchorGameObject, surfaceNormal, photonView);
                }
                else
                {
                    //ttp.StartPlacement();
                    sm.enabled = true;

                    // make invisible and show ghost hold instead
                    //anchorGameObject.GetComponent<MeshRenderer>().enabled = false;
                    anchorGameObject.GetComponent<OnHoldMove>().OnMoveStart();
                    ghost = PhotonNetwork.Instantiate(hold + "_Ghost", handPosition, Quaternion.identity);
                }
            }
            else if (anchorNearby && editingMode == EditingMode.Delete)
            {
                // Delete nearby Anchor
                DeleteAnchor(anchorGameObject);
            }
        }
        // </ShortTap>

        // <LongTap>
        /// <summary>
        /// Called when a user is air tapping for a long time (>=2 sec)
        /// </summary>
        private async void LongTap()
        {
            Debug.Log("LongTap");

            if (editingMode == EditingMode.Delete)
            {
                PhotonView pv = this.gameObject.GetPhotonView();
                pv.RPC("PunRPC_RemoveAllAnchorGameObjects", RpcTarget.MasterClient);
            }
        }
        // </LongTap>

        // <RemoveAllAnchorGameObjects>
        /// <summary>
        /// Destroys all Anchor GameObjects
        /// </summary>
        [PunRPC]
        private async void PunRPC_RemoveAllAnchorGameObjects()
        {
            indicatorObject.gameObject.SetActive(true);
            _foundOrCreatedAnchorGameObjects = GameObject.FindGameObjectsWithTag("Hold");
            foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
            {
                PhotonView pv = anchorGameObject.GetComponent<PhotonView>();
                Debug.Log($"pv: {pv}");
                pv.RequestOwnership(); // we need ownership of the object to destroy it
            }

            // the RequestOwnership calls above are asynchronous and need time to complete before we call Destroy() below
            await Task.Delay(1000);
            
            foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
            {
                PhotonView pv = anchorGameObject.GetComponent<PhotonView>();
                PhotonNetwork.Destroy(anchorGameObject);
            }
            indicatorObject.gameObject.SetActive(false);
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
            _foundOrCreatedAnchorGameObjects = GameObject.FindGameObjectsWithTag("Hold");
            Debug.Log($"IsAnchorNearby -> number of objects in scene: {_foundOrCreatedAnchorGameObjects.Length}");

            if (_foundOrCreatedAnchorGameObjects.Length <= 0)
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

        // <HoldOnPlacingStarted>
        /// <summary>
        /// Called on start of movement of object
        /// </summary>
        /// <param name="go"></param>
        private void HoldOnPlacingStarted(GameObject go)
        {
            Debug.Log("HoldOnPlacingStarted");
        }
        // </HoldOnPlacingStarted>

        // <HoldOnPlacingStopped>
        /// <summary>
        /// Handles object saving once user has finished moving an object.
        /// Called for surface magnetism movement
        /// </summary>
        /// <param name="go"></param>
        /// <returns></returns>
        private async Task HoldOnPlacingStopped(GameObject go, Vector3 surfaceNormal, PhotonView photonView)
        {
            Debug.Log("HoldOnPlacingStopped");

            Vector3 position = go.transform.position;

            // the game object may have been moved to a new surface necessitating a change of its local coordinate frame (i.e. its z-axis now has a negative dot-product with the
            // Frozen coordinate frame meaning that on manipulation, its rotation will be counter to whats expected)
            // So we destroy it and recreate it (since the logic for checking this dot-product will be contained within CreateAnchor anyway)
            DeleteAnchor(go);
            CreateAnchor(position, surfaceNormal, photonView);
        }
        // </HoldOnPlacingStopped>

        // <HoldOnHoverStartedHandler>
        /// <summary>
        /// OnHoverStarted handler for hold GameObject.
        /// Changes the color.
        /// </summary>
        /// <param name="eventdata"></param>
        /// <param name="hold"></param>
        private void HoldOnHoverStartedHandler(ManipulationEventData eventdata, GameObject hold)
        {
            MeshRenderer m_Renderer = hold.GetComponent<MeshRenderer>();
            m_Renderer.material.color = Color.red;
        }
        // </HoldOnHoverStartedHandler>

        // <HoldOnHoverExitedHandler>
        /// <summary>
        /// OnHoverExited handler for hold GameObjects.
        /// Changes the color.
        /// </summary>
        /// <param name="eventdata"></param>
        /// <param name="hold"></param>
        private void HoldOnHoverExitedHandler(ManipulationEventData eventdata, GameObject hold)
        {
            MeshRenderer m_Renderer = hold.GetComponent<MeshRenderer>();
            m_Renderer.material.color = Color.green;
        }
        // </HoldOnHoverExitedHandler>

        //// <HoldOnManipulationStartedHandler>
        ///// <summary>
        ///// Handles saving object once user has finished rotating an object
        ///// Called by manipulation script event for rotation
        ///// </summary>
        ///// <param name="eventData"></param>
        ///// <param name="hold"></param>
        //private void HoldOnManipulationStartedHandler(ManipulationEventData eventData, GameObject hold)
        //{
        //    Debug.Log($"HoldOnManipulationStarted");

        //    // detect if object is receiving a short tap
        //    eventData.ManipulationSource.GetComponent<HoldData>().manipulationStartTime = DateTime.Now;

        //    // the cursor doesn't always stay aligned with object's mesh when moving it with TapToPlace
        //    MeshRenderer m_Renderer = hold.GetComponent<MeshRenderer>();
        //    m_Renderer.material.color = Color.red;

        //    //// detect if object is receiving a short tap
        //    //bool receivingTap = false;
        //    //for (int i = 0; i < 2; i++)
        //    //{
        //    //    InputDevice device = InputDevices.GetDeviceAtXRNode((i == 0) ? XRNode.RightHand : XRNode.LeftHand);
        //    //    device.TryGetFeatureValue(CommonUsages.primaryButton, out bool handIsTapping);
        //    //    receivingTap = receivingTap || handIsTapping;
        //    //    Debug.Log(handIsTapping);
        //    //}
        //    //eventData.ManipulationSource.GetComponent<HoldData>().receivingShortTap = receivingTap;
        //}
        //// </HoldOnManipulationStartedHandler>

        //// <HoldOnManipulationEndedHandler>
        ///// <summary>
        ///// OnManipulationEndedHandler for GameObject.
        ///// Saves final position.
        ///// We don't want this called for short taps as those are reserved for movement corresponding to surface magnetism.
        ///// </summary>
        ///// <param name="eventData"></param>
        ///// <param name="currentAnchorGameObject"></param>
        //private async void HoldOnManipulationEndedHandler(ManipulationEventData eventData, GameObject currentAnchorGameObject)
        //{
        //    Debug.Log($"HoldOnManipulationEnded");

        //    DateTime currentTime = DateTime.Now;
        //    TimeSpan interval = currentTime - eventData.ManipulationSource.GetComponent<HoldData>().manipulationStartTime;
        //    Debug.Log(interval.TotalMilliseconds);

        //    // ignore short taps (shorter than 1 second) from either hand
        //    if (interval.TotalMilliseconds >= 1000)
        //    //if (!eventData.ManipulationSource.GetComponent<HoldData>().receivingShortTap)
        //    {
        //        Debug.Log("Detected long tap as manipulation event");

        //        Vector3 position = eventData.ManipulationSource.transform.position;
        //        Quaternion rotation = eventData.ManipulationSource.transform.rotation;
        //        Vector3 localScale = eventData.ManipulationSource.transform.localScale;

        //        DeleteAnchor(eventData.ManipulationSource);

        //        await BuildAnchor(hold, position, rotation, localScale);
        //    }
        //}
        //// </HoldOnManipulationEndedHandler>

        // <CreateAnchor>
        /// <summary>
        /// Creates an Azure Spatial Anchor at the given position rotated towards the user
        /// </summary>
        /// <param name="position">Position where Azure Spatial Anchor will be created</param>
        /// <returns>Async Task</returns>
        private void CreateAnchor(Vector3 position, Vector3 surfaceNormal, PhotonView photonView)
        {
            Debug.Log($"CreateAnchor");

            //Create Anchor GameObject. We will use ASA to save the position and the rotation of this GameObject.
            if (!InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition))
            {
                headPosition = Vector3.zero;
            }

            Quaternion normalOrientation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);
            float normalDotProduct = Vector3.Dot(surfaceNormal, GameObject.Find("F1").transform.forward);

            Debug.Log($"Normal Orientation: {surfaceNormal}");
            Debug.Log($"Dot Product with Normal: {Vector3.Dot(surfaceNormal, GameObject.Find("F1").transform.forward)}");            

            string hold_version = hold;
            if (normalDotProduct > 0)
            {
                hold_version = hold + "_Flipped_New";
                normalOrientation = Quaternion.LookRotation(surfaceNormal, Vector3.up);
            }

            Debug.Log($"Placing: {hold_version}");

            photonView.RPC("BuildAnchor", RpcTarget.MasterClient, hold_version, position, normalOrientation, Vector3.one * 0.1f);
        }
        // </CreateAnchor>

        [PunRPC]
        void BuildAnchor(string go, Vector3 position, Quaternion rotation, Vector3 localScale)
        {
            // InstantiateRoomObject only succeeds for master client 
            if (PhotonNetwork.IsMasterClient)
            {
                // open loader
                indicatorObject.SetActive(true);

                //// Temporarily disable MRTK input because this function is async and could be called in quick succession with race issues.  Last answer here: https://stackoverflow.com/questions/56757620/how-to-temporarly-disable-mixedrealitytoolkit-inputsystem
                //StartCoroutine(DisableCoroutine());

                GameObject newAnchorGameObject = PhotonNetwork.InstantiateRoomObject(go, position, rotation);

                Debug.Log(newAnchorGameObject);

                newAnchorGameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
                newAnchorGameObject.transform.position = position;
                newAnchorGameObject.transform.rotation = rotation;
                newAnchorGameObject.transform.localScale = localScale;

                Debug.Log($"Forward Direction of Object: {newAnchorGameObject.transform.forward}");
                Debug.Log($"Forward Direction of Frozen Frame: {GameObject.Find("F1").transform.forward}");
                Debug.Log($"Dot Product with Object z: {Vector3.Dot(newAnchorGameObject.transform.forward, GameObject.Find("F1").transform.forward)}");

                ////Add and configure ASA components
                //CloudNativeAnchor cloudNativeAnchor = newAnchorGameObject.AddComponent<CloudNativeAnchor>();
                //await cloudNativeAnchor.NativeToCloud();
                //CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;
                //cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(3);

                ////Collect Environment Data
                //while (!_spatialAnchorManager.IsReadyForCreate)
                //{
                //    float createProgress = _spatialAnchorManager.SessionStatus.RecommendedForCreateProgress;
                //    Debug.Log($"ASA - Move your device to capture more environment data: {createProgress:0%}");
                //}

                //Debug.Log($"ASA - Saving cloud anchor... ");

                //_foundOrCreatedAnchorGameObjects.Add(newAnchorGameObject);
                //_createdAnchorIDs.Add(cloudSpatialAnchor.Identifier);
                //newAnchorGameObject.GetComponent<MeshRenderer>().material.color = Color.green;

                // Disable maninpulation scripts if we are in 'Delete' mode
                if (editingMode == EditingMode.Delete)
                {
                    newAnchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
                    newAnchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
                }

                // close loader
                indicatorObject.SetActive(false);
            }
            //// open loader
            //indicatorObject.SetActive(true);

            ////// Temporarily disable MRTK input because this function is async and could be called in quick succession with race issues.  Last answer here: https://stackoverflow.com/questions/56757620/how-to-temporarly-disable-mixedrealitytoolkit-inputsystem
            ////StartCoroutine(DisableCoroutine());

            //Debug.Log("There");

            ////GameObject newAnchorGameObject = Instantiate(hold);
            //GameObject newAnchorGameObject = PhotonNetwork.InstantiateRoomObject(go, position, rotation);
            ////GameObject newAnchorGameObject = PhotonNetwork.Instantiate(go, position, rotation);

            //Debug.Log(newAnchorGameObject);

            //newAnchorGameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
            //newAnchorGameObject.transform.position = position;
            //newAnchorGameObject.transform.rotation = rotation;
            //newAnchorGameObject.transform.localScale = localScale;

            //Debug.Log("Here");

            //////Add and configure ASA components
            ////CloudNativeAnchor cloudNativeAnchor = newAnchorGameObject.AddComponent<CloudNativeAnchor>();
            ////await cloudNativeAnchor.NativeToCloud();
            ////CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;
            ////cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(3);

            //////Collect Environment Data
            ////while (!_spatialAnchorManager.IsReadyForCreate)
            ////{
            ////    float createProgress = _spatialAnchorManager.SessionStatus.RecommendedForCreateProgress;
            ////    Debug.Log($"ASA - Move your device to capture more environment data: {createProgress:0%}");
            ////}

            ////Debug.Log($"ASA - Saving cloud anchor... ");

            ////_foundOrCreatedAnchorGameObjects.Add(newAnchorGameObject);
            ////_createdAnchorIDs.Add(cloudSpatialAnchor.Identifier);
            //newAnchorGameObject.GetComponent<MeshRenderer>().material.color = Color.green;

            //// Disable maninpulation scripts if we are in 'Delete' mode
            //if (editingMode == EditingMode.Delete)
            //{
            //    newAnchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
            //    newAnchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
            //}

            //// close loader
            //indicatorObject.SetActive(false);

            // share the created holds for other clients to be able to detect them on short tap
            //ShareHolds();

            //try
            //{
            //    // Now that the cloud spatial anchor has been prepared, we can try the actual save here.
            //    //await _spatialAnchorManager.CreateAnchorAsync(cloudSpatialAnchor);

            //    //bool saveSucceeded = cloudSpatialAnchor != null;
            //    //if (!saveSucceeded)
            //    //{
            //    //    Debug.LogError("ASA - Failed to save, but no exception was thrown.");
            //    //    return;
            //    //}

            //    //Debug.Log($"ASA - Saved cloud anchor with ID: {cloudSpatialAnchor.Identifier}");
            //    _foundOrCreatedAnchorGameObjects.Add(newAnchorGameObject);
            //    //_createdAnchorIDs.Add(cloudSpatialAnchor.Identifier);
            //    newAnchorGameObject.GetComponent<MeshRenderer>().material.color = Color.green;

            //    // add manipulation scripts: https://stackoverflow.com/questions/61663652/adding-manipulation-components-via-c-sharp-script-only-works-in-unity
            //    Debug.Log($"Adding manipulation");
            //    Mesh mesh = newAnchorGameObject.GetComponent<MeshFilter>().mesh;

            //    //Add MeshCollider
            //    MeshCollider collider = newAnchorGameObject.EnsureComponent<MeshCollider>();

            //    //A lot of components are curved and need convex set to false
            //    collider.convex = true;

            //    //Add NearInteractionGrabbable
            //    newAnchorGameObject.EnsureComponent<NearInteractionGrabbable>();

            //    //Add ManipulationHandler with event listeners
            //    Debug.Log(newAnchorGameObject.transform.position);
            //    Debug.Log(newAnchorGameObject.transform.rotation);
            //    var handler = newAnchorGameObject.EnsureComponent<ObjectManipulator>();
            //    handler.OnHoverEntered.AddListener((eventData) => HoldOnHoverStartedHandler(eventData, newAnchorGameObject));
            //    handler.OnHoverExited.AddListener((eventData) => HoldOnHoverExitedHandler(eventData, newAnchorGameObject));
            //    handler.OnManipulationStarted.AddListener((eventData) => HoldOnManipulationStartedHandler(eventData, newAnchorGameObject));
            //    handler.OnManipulationEnded.AddListener((eventData) => HoldOnManipulationEndedHandler(eventData, newAnchorGameObject));

            //    //Add TapToPlace event listeners
            //    TapToPlace ttp = newAnchorGameObject.EnsureComponent<TapToPlace>();
            //    ttp.OnPlacingStarted.AddListener(() => HoldOnPlacingStarted(newAnchorGameObject));
            //    ttp.OnPlacingStopped.AddListener(() => HoldOnPlacingStopped(newAnchorGameObject));

            //    ////SurfaceMagnetism sm = newAnchorGameObject.EnsureComponent<SurfaceMagnetism>();
            //    ////sm.enabled = false;

            //    //Set mesh to MeshCollider
            //    collider.sharedMesh = mesh;

            //    // Disable maninpulation scripts if we are in 'Delete' mode
            //    if (editingMode == EditingMode.Delete)
            //    {
            //        newAnchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
            //        newAnchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
            //    }

            //    //// share the created anchorId
            //    //ShareAzureAnchorIds();
            //}
            //catch (Exception exception)
            //{
            //    Debug.Log("ASA - Failed to save anchor: " + exception.ToString());
            //    Debug.LogException(exception);
            //}

            //// turn on MRTK inputs
            //StartCoroutine(EnableCoroutine());

            //// the gaze pointer comes back along with hand pointer so we disable the gaze pointer: https://docs.microsoft.com/en-us/windows/mixed-reality/mrtk-unity/mrtk2/features/input/pointers?view=mrtkunity-2021-05#disable-pointers
            //PointerUtils.SetGazePointerBehavior(PointerBehavior.AlwaysOff); // gives a warning concerning WindowsMixedReality inputs but so far seems OK to ignore

            // close loader
            //indicatorObject.SetActive(false);
        }

        //// <LocateAnchor>
        ///// <summary>
        ///// Looking for anchors with ID in _createdAnchorIDs
        ///// </summary>
        //private void LocateAnchor()
        //{
        //    if (_createdAnchorIDs.Count > 0)
        //    {
        //        //Create watcher to look for all stored anchor IDs
        //        Debug.Log($"ASA - Creating watcher to look for {_createdAnchorIDs.Count} spatial anchors");
        //        AnchorLocateCriteria anchorLocateCriteria = new AnchorLocateCriteria();
        //        anchorLocateCriteria.Identifiers = _createdAnchorIDs.ToArray();
        //        var watcher = _spatialAnchorManager.Session.CreateWatcher(anchorLocateCriteria);
        //        Debug.Log($"ASA - Watcher created!");
        //    }
        //}
        //// </LocateAnchor>

        //// <SpatialAnchorManagerAnchorLocated>
        ///// <summary>
        ///// Callback when an anchor is located
        ///// </summary>
        ///// <param name="sender">Callback sender</param>
        ///// <param name="args">Callback AnchorLocatedEventArgs</param>
        //private void SpatialAnchorManager_AnchorLocated(object sender, AnchorLocatedEventArgs args)
        //{
        //    Debug.Log($"ASA - Anchor recognized as a possible anchor {args.Identifier} {args.Status}");

        //    if (args.Status == LocateAnchorStatus.Located)
        //    {
        //        //Creating and adjusting GameObjects have to run on the main thread. We are using the UnityDispatcher to make sure this happens.
        //        UnityDispatcher.InvokeOnAppThread(() =>
        //        {
        //            // Read out Cloud Anchor values
        //            CloudSpatialAnchor cloudSpatialAnchor = args.Anchor;

        //            //Create GameObject
        //            //GameObject anchorGameObject = Instantiate(hold);
        //            GameObject anchorGameObject = PhotonNetwork.InstantiateRoomObject(hold, Vector3.zero, Quaternion.identity);
        //            //GameObject anchorGameObject = PhotonNetwork.Instantiate(hold, Vector3.zero, Quaternion.identity);
        //            anchorGameObject.transform.localScale = Vector3.one * 0.1f;
        //            anchorGameObject.GetComponent<MeshRenderer>().material.shader = Shader.Find("Legacy Shaders/Diffuse");
        //            anchorGameObject.GetComponent<MeshRenderer>().material.color = Color.blue;

        //            // change color to green after a delay
        //            StartCoroutine(ChangeColorDelayed(anchorGameObject, Color.green, 2f));

        //            // Link to Cloud Anchor
        //            anchorGameObject.AddComponent<CloudNativeAnchor>().CloudToNative(cloudSpatialAnchor);
        //            _foundOrCreatedAnchorGameObjects.Add(anchorGameObject);

        //            // Add and then disable maninpulation scripts since we are in 'Delete' mode
        //            Debug.Log($"Adding manipulation");
        //            Mesh mesh = anchorGameObject.GetComponent<MeshFilter>().mesh;

        //            //Add MeshCollider
        //            MeshCollider collider = anchorGameObject.EnsureComponent<MeshCollider>();

        //            //A lot of components are curved and need convex set to false
        //            collider.convex = true;

        //            //Add NearInteractionGrabbable
        //            anchorGameObject.EnsureComponent<NearInteractionGrabbable>();

        //            //Add manipulation event listeners
        //            var omHandler = anchorGameObject.EnsureComponent<ObjectManipulator>();
        //            omHandler.OnHoverEntered.AddListener((eventData) => HoldOnHoverStartedHandler(eventData, anchorGameObject));
        //            omHandler.OnHoverExited.AddListener((eventData) => HoldOnHoverExitedHandler(eventData, anchorGameObject));
        //            omHandler.OnManipulationStarted.AddListener((eventData) => HoldOnManipulationStartedHandler(eventData, anchorGameObject));
        //            omHandler.OnManipulationEnded.AddListener((eventData) => HoldOnManipulationEndedHandler(eventData, anchorGameObject));

        //            ////Add TapToPlace event listeners
        //            //TapToPlace ttp = anchorGameObject.EnsureComponent<TapToPlace>();
        //            //ttp.OnPlacingStarted.AddListener(() => HoldOnPlacingStarted(anchorGameObject));
        //            //ttp.OnPlacingStopped.AddListener(() => HoldOnPlacingStopped(anchorGameObject));

        //            //Set mesh to MeshCollider
        //            collider.sharedMesh = mesh;

        //            // Disable maninpulation scripts if we are in 'Delete' mode
        //            if (editingMode == EditingMode.Delete)
        //            {
        //                anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
        //                anchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
        //            }
        //            //// Disable maninpulation scripts since we are in 'Delete' mode
        //            //anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
        //            //anchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
        //        });
        //    }
        //}
        //// </SpatialAnchorManagerAnchorLocated>

        // <DeleteAnchor>
        /// <summary>
        /// Deleting Cloud Anchor attached to the given GameObject and deleting the GameObject
        /// </summary>
        /// <param name="anchorGameObject">Anchor GameObject that is to be deleted</param>
        private void DeleteAnchor(GameObject anchorGameObject)
        {
            if (anchorGameObject != null)
            {
                //CloudNativeAnchor cloudNativeAnchor = anchorGameObject.GetComponent<CloudNativeAnchor>();
                //CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;

                //Debug.Log($"ASA - Deleting cloud anchor: {cloudSpatialAnchor.Identifier}");

                ////Request Deletion of Cloud Anchor
                //await _spatialAnchorManager.DeleteAnchorAsync(cloudSpatialAnchor);

                ////Remove local references
                //_createdAnchorIDs.Remove(cloudSpatialAnchor.Identifier);
                //_foundOrCreatedAnchorGameObjects = GameObject.FindGameObjectsWithTag("Hold");
                //_foundOrCreatedAnchorGameObjects.Remove(anchorGameObject);
                //Destroy(anchorGameObject);
                PhotonNetwork.Destroy(anchorGameObject);

                Debug.Log($"ASA - Cloud anchor deleted!");
            }
        }
        // </DeleteAnchor>

        // <ToggleEditingMode>
        /// <summary>
        /// Toggles editing mode between Move and Delete
        /// </summary>
        public void ToggleEditingMode()
        {
            _foundOrCreatedAnchorGameObjects = GameObject.FindGameObjectsWithTag("Hold");
            if (editingMode == EditingMode.Move)
            {
                editingMode = EditingMode.Delete;

                // make holds unmovable
                foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
                {
                    Debug.Log($"Disabling Manipulation");
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

                GameObject.Find("ToggleEditorMode").GetComponentInChildren<TextMeshPro>().text = "Mode: Delete";
            }
            else if (editingMode == EditingMode.Delete)
            {
                editingMode = EditingMode.Move;

                // make holds movable
                foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
                {
                    // enable manipulation scripts
                    Debug.Log($"Enabling Manipulation");
                    anchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = true;
                    anchorGameObject.GetComponent<ObjectManipulator>().enabled = true;
                }

                GameObject.Find("ToggleEditorMode").GetComponentInChildren<TextMeshPro>().text = "Mode: Move";
            }
        }
        // </ToggleEditingMode>

        //// <changeColorDelayed>
        ///// <summary>
        ///// Change color of game object.
        ///// Used in delayed fashion.
        ///// </summary>
        ///// <param name="gameObject"></param>
        //private IEnumerator ChangeColorDelayed(GameObject gameObject, Color color, float time)
        //{
        //    yield return new WaitForSeconds(time);
        //    gameObject.GetComponent<MeshRenderer>().material.color = color;
        //}
        //// </changeColorDelayed>

        // <destroyObjectDelayed>
        /// <summary>
        /// Destroy object.
        /// Used in delayed fashion e.g. when removing long tap's sphere object created for user feedback
        /// </summary>
        /// <param name="gameObject"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        private IEnumerator DestroyObjectDelayed(GameObject gameObject, float time)
        {
            yield return new WaitForSeconds(time);
            //Destroy(gameObject);
            PhotonNetwork.Destroy(gameObject);
        }
        // </destroyObjectDelayed>

        //// <DisableCoroutine>
        ///// <summary>
        ///// Disable MRTK input 
        ///// </summary>
        ///// <returns></returns>
        //private IEnumerator DisableCoroutine()
        //{
        //    yield return null;
        //    Microsoft.MixedReality.Toolkit.CoreServices.InputSystem.Disable();
        //}
        //// </DisableCoroutine>

        //// <EnableCoroutine>
        ///// <summary>
        ///// Enable MRTK input
        ///// </summary>
        ///// <returns></returns>
        //private IEnumerator EnableCoroutine()
        //{
        //    yield return null;
        //    Microsoft.MixedReality.Toolkit.CoreServices.InputSystem.Enable();
        //}
        //// </EnableCoroutine>

        // <ScrollHoldMenuClick>
        /// <summary>
        /// Handle scoll hold menu selection
        /// </summary>
        /// <param name="go"></param>
        public void ScrollHoldMenuClick(GameObject go)
        {
            Debug.Log(go);
            if (go != null)
            {
                // PhotonNetwork.PrefabPool lets us refer to prefabs by name under Resources folder without having to manually add them to the ResourceCache: https://forum.unity.com/threads/solved-photon-instantiating-prefabs-without-putting-them-in-a-resources-folder.293853/
                hold = $"{go.name}";
            }
        }

        //private async void restartAnchorWatcher(List<string> anchorIds)
        //{
        //    Debug.Log("restartAnchorWatcher");
        //    _createdAnchorIDs = anchorIds;

        //    // there doesn't appear to be a method to simply remove the watcher so we need to stop/restart the session and reattach a new watcher for the updated list of anchorIds
        //    if (_spatialAnchorManager.IsSessionStarted)
        //    {
        //        // Stop Session and remove all GameObjects. This does not delete the Anchors in the cloud
        //        _spatialAnchorManager.DestroySession();
        //        RemoveAllAnchorGameObjects();
        //        Debug.Log("ASA - Stopped Session and removed all Anchor Objects");
        //    }

        //    //Start session and search for all Anchors previously created
        //    await _spatialAnchorManager.StartSessionAsync();
        //    LocateAnchor();
        //}

//        public void ShareAzureAnchorIds()
//        {
//            Debug.Log("ShareAzureAnchorIds");
//#if UNITY_2020
//            ExitGames.Client.Photon.Hashtable setValue = new ExitGames.Client.Photon.Hashtable();
//            setValue.Add("anchorIDs", _createdAnchorIDs.ToArray());
//            PhotonNetwork.CurrentRoom.SetCustomProperties(setValue);
//            //if (player != null)
//            //    player.GetComponent<PhotonView>().RPC("PunRPC_ShareAzureAnchorIds", RpcTarget.OthersBuffered, _createdAnchorIDs);
//            //else
//            //    Debug.LogError("PV is null");
//#endif
//        }

//        public void ShareHolds()
//        {
//            Debug.Log("ShareObjects");
//#if UNITY_2020
//            ExitGames.Client.Photon.Hashtable setValue = new ExitGames.Client.Photon.Hashtable();
//            setValue.Add("holds", _foundOrCreatedAnchorGameObjects);
//            PhotonNetwork.CurrentRoom.SetCustomProperties(setValue);
//#endif
//        }

//        public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
//        {
//            base.OnRoomPropertiesUpdate(propertiesThatChanged);

//            Debug.Log("OnRoomPropertiesUpdate");
//            //if (propertiesThatChanged.ContainsKey("anchorIDs"))
//            //{
//            //    String[] anchorIDs = (String[])propertiesThatChanged["anchorIDs"];
//            //    restartAnchorWatcher(anchorIDs.ToList());
//            //}
//            if (propertiesThatChanged.ContainsKey("holds"))
//            {
//                _foundOrCreatedAnchorGameObjects = (List<GameObject>)propertiesThatChanged["holds"];
//            }
//        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.AddCallbackTarget(this);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        //public async Task<string> CreateAnchorOnObjectAsync(GameObject gameObjectForAnchor)
        //{
        //    Debug.Log("CreateAnchorOnObjectAsync");

        //    string anchorId = string.Empty;

        //    await _spatialAnchorManager.StartSessionAsync();

        //    //Add and configure ASA components
        //    CloudNativeAnchor cloudNativeAnchor = gameObjectForAnchor.AddComponent<CloudNativeAnchor>();
        //    await cloudNativeAnchor.NativeToCloud();
        //    CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;
        //    cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(3);

        //    //Collect Environment Data
        //    while (!_spatialAnchorManager.IsReadyForCreate)
        //    {
        //        float createProgress = _spatialAnchorManager.SessionStatus.RecommendedForCreateProgress;
        //        Debug.Log($"ASA - Move your device to capture more environment data: {createProgress:0%}");
        //    }

        //    Debug.Log($"ASA - Saving room cloud anchor... ");

        //    try
        //    {
        //        // Now that the cloud spatial anchor has been prepared, we can try the actual save here.
        //        await _spatialAnchorManager.CreateAnchorAsync(cloudSpatialAnchor);

        //        bool saveSucceeded = cloudSpatialAnchor != null;
        //        if (!saveSucceeded)
        //        {
        //            Debug.LogError("ASA - Failed to save, but no exception was thrown.");
        //            return anchorId;
        //        }

        //        anchorId = cloudSpatialAnchor.Identifier;
        //        Debug.Log($"ASA - Saved room cloud anchor with ID: {anchorId}");
        //    }
        //    catch (Exception exception)
        //    {
        //        Debug.Log("ASA - Failed to save room anchor: " + exception.ToString());
        //        Debug.LogException(exception);
        //    }

        //    return anchorId;
        //}

        //public async Task<bool> PopulateAnchorOnObjectAsync(string anchorId, GameObject gameObjectForAnchor)
        //{
        //    Debug.Log("PopulateAnchorOnObjectAsync");

        //    bool anchorLocated = false;

        //    try
        //    {
        //        await _spatialAnchorManager.StartSessionAsync();

        //        this.taskWaitForAnchorLocation = new TaskCompletionSource<CloudSpatialAnchor>();

        //        var watcher = _spatialAnchorManager.Session.CreateWatcher(
        //            new AnchorLocateCriteria()
        //            {
        //                Identifiers = new string[] { anchorId },
        //                BypassCache = true,
        //                Strategy = LocateStrategy.AnyStrategy,
        //                RequestedCategories = AnchorDataCategory.Spatial
        //            }
        //        );

        //        var cloudAnchor = await this.taskWaitForAnchorLocation.Task;

        //        anchorLocated = cloudAnchor != null;

        //        if (anchorLocated)
        //        {
        //            gameObjectForAnchor.AddComponent<CloudNativeAnchor>().CloudToNative(cloudAnchor);
        //        }
        //        watcher.Stop();
        //    }
        //    catch (Exception ex) // TODO: reasonable exceptions here.
        //    {
        //        Debug.Log($"Caught {ex.Message}");
        //    }
        //    return (anchorLocated);
        //}
    }
}
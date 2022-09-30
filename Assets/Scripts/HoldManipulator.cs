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

namespace Scripts
{
    public class HoldManipulator : MonoBehaviourPunCallbacks
    {
        /// <summary>
        /// Used to distinguish short taps and long taps
        /// </summary>
        private float[] _tappingTimer = { 0, 0 };

        /// <summary>
        /// Main interface to anything Spatial Anchors related
        /// </summary>
        //private SpatialAnchorManager _spatialAnchorManager = null;

        /// <summary>
        /// Used to keep track of all GameObjects that represent a found or created anchor
        /// </summary>
        private GameObject[] _foundOrCreatedAnchorGameObjects;

        ///// <summary>
        ///// Used to keep track of all the created Anchor IDs
        ///// </summary>
        //private List<String> _createdAnchorIDs = new List<String>();

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
        /// hold prefab
        /// </summary>
        private string hold;

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

        /// <summary>
        /// GameObject to represent currently chosen hold during movement of that hold
        /// </summary>
        private GameObject ghost = default;

        TaskCompletionSource<CloudSpatialAnchor> taskWaitForAnchorLocation;

        // we have to apply audio sources for create/delete/moveStart/moveEnd events here because we will be disabling/enabling manipulation components depending on the
        // mode we are in
        // manipulation audio (i.e. rotation) is supplied by the manipulation component since it only applies in the mode when the manipulation component is enabled
        [SerializeField]
        private AudioSource audioData;

        [SerializeField]
        private AudioClip createAudio;

        [SerializeField]
        private AudioClip deleteOneAudio;

        [SerializeField]
        private AudioClip deleteAllAudio;

        [SerializeField]
        private AudioClip moveStartAudio;

        [SerializeField]
        private AudioClip moveEndAudio;

        // <Start>
        // Start is called before the first frame update
        void Start()
        {
            editingMode = EditingMode.Move;
            GameObject.Find("ToggleEditorMode").GetComponentInChildren<TextMeshPro>().text = "Mode: Move";
            indicatorObject.SetActive(false);
            hold = "Hold_1_Simple";
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
                                                    GameObject gameObject = PhotonNetwork.Instantiate(longTapSphere.name, endPoint, Quaternion.identity);
                                                    gameObject.transform.position = endPoint;
                                                    gameObject.transform.rotation = Quaternion.identity;
                                                    gameObject.transform.localScale = Vector3.one * 0.05f;

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
                audioData.PlayOneShot(createAudio);

                // No Anchor Nearby, start session and create an anchor
                CreateAnchor(handPosition, surfaceNormal, photonView);
            }
            else if (anchorNearby && editingMode == EditingMode.Move)
            {
                //// Toggle TapToPlace on so we can start or end moving the object
                ////anchorGameObject.GetComponent<TapToPlace>().enabled = !anchorGameObject.GetComponent<TapToPlace>().enabled;
                ////TapToPlace ttp = anchorGameObject.GetComponent<TapToPlace>();

                // toggle surface magnetism component so we can start or end moving the object
                bool isTappingToPlace = anchorGameObject.GetComponent<HoldData>().isTappingToPlace;
                anchorGameObject.GetComponent<HoldData>().isTappingToPlace = !anchorGameObject.GetComponent<HoldData>().isTappingToPlace;
                SurfaceMagnetism sm = anchorGameObject.EnsureComponent<SurfaceMagnetism>();
               
                if (isTappingToPlace)
                {
                    audioData.PlayOneShot(moveEndAudio);

                    //ttp.StopPlacement();
                    sm.enabled = false;

                    // make visible and remove ghost hold
                    PhotonNetwork.Destroy(ghost);
                    anchorGameObject.GetComponent<OnHoldMove>().OnMoveEnd();
                    await HoldOnPlacingStopped(anchorGameObject, surfaceNormal, photonView);
                }
                else
                {
                    audioData.PlayOneShot(moveStartAudio);

                    //ttp.StartPlacement();
                    sm.enabled = true;

                    // make invisible and show ghost hold instead
                    anchorGameObject.GetComponent<OnHoldMove>().OnMoveBegin();
                    ghost = PhotonNetwork.Instantiate(hold + "_Ghost", handPosition, Quaternion.identity);
                }
            }
            else if (anchorNearby && editingMode == EditingMode.Delete)
            {
                audioData.PlayOneShot(deleteOneAudio);

                // Delete nearby Anchor
                DeleteGameObject(anchorGameObject);
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
                audioData.PlayOneShot(deleteAllAudio);
                PhotonView pv = this.gameObject.GetPhotonView();
                pv.RPC("PunRPC_RemoveAllAnchorGameObjects", RpcTarget.MasterClient);
            }
        }
        // </LongTap>

        // <RemoveAllAnchorGameObjects>
        /// <summary>
        /// Destroys all Anchor GameObjects.
        /// </summary>
        [PunRPC]
        private async void PunRPC_RemoveAllAnchorGameObjects()
        {
            indicatorObject.gameObject.SetActive(true);
            _foundOrCreatedAnchorGameObjects = GameObject.FindGameObjectsWithTag("Hold");
            foreach (var anchorGameObject in _foundOrCreatedAnchorGameObjects)
            {
                PhotonView pv = anchorGameObject.GetComponent<PhotonView>();
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
            Vector3 position = go.transform.position;

            // the game object may have been moved to a new surface necessitating a change of its local coordinate frame (i.e. its z-axis now has a negative dot-product with the
            // Frozen coordinate frame meaning that on manipulation, its rotation will be counter to whats expected)
            // So we destroy it and recreate it (since the logic for checking this dot-product will be contained within CreateAnchor anyway)
            DeleteGameObject(go);
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

        //        DeleteGameObject(eventData.ManipulationSource);

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
                hold_version = hold + "_Flipped";
                normalOrientation = Quaternion.LookRotation(surfaceNormal, Vector3.up);
            }

            Debug.Log($"Placing: {hold_version}");

            // InstantiateRoomObject only succeeds for master client
            List<string> customTags = new List<string> { };
            string customTagsString = string.Join(",", customTags); // PUN2 doesn't support arrays/lists as parameters
            photonView.RPC("BuildAnchor", RpcTarget.MasterClient, hold_version, position, normalOrientation, Vector3.one * 0.1f, customTagsString);
        }
        // </CreateAnchor>

        [PunRPC]
        void BuildAnchor(string go, Vector3 position, Quaternion rotation, Vector3 localScale, string customTagsString)
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
            //newAnchorGameObject.transform.localScale = localScale; // don't set to localScale because we need to be able to parent the holds arbitrarily and this line could make them either very small or large

            // set any custom tags on all clients (e.g. necessary for when instantiating hold configs)
            // NOTE: without RPC, the custom tags would only be set for this client
            newAnchorGameObject.GetComponent<PhotonView>().RPC("PunRPC_SetCustomTags", RpcTarget.All, customTagsString);

            Debug.Log($"Forward Direction of Object: {newAnchorGameObject.transform.forward}");
            Debug.Log($"Forward Direction of Frozen Frame: {GameObject.Find("F1").transform.forward}");
            Debug.Log($"Dot Product with Object z: {Vector3.Dot(newAnchorGameObject.transform.forward, GameObject.Find("F1").transform.forward)}");

            // Disable maninpulation scripts if we are in 'Delete' mode
            if (editingMode == EditingMode.Delete)
            {
                newAnchorGameObject.GetComponent<NearInteractionGrabbable>().enabled = false;
                newAnchorGameObject.GetComponent<ObjectManipulator>().enabled = false;
            }

            // close loader
            indicatorObject.SetActive(false);
        }

        // <DeleteGameObject>
        /// <summary>
        /// Deleting Cloud Anchor attached to the given GameObject and deleting the GameObject
        /// </summary>
        /// <param name="anchorGameObject">Anchor GameObject that is to be deleted</param>
        private void DeleteGameObject(GameObject anchorGameObject)
        {
            if (anchorGameObject != null)
            {
                PhotonNetwork.Destroy(anchorGameObject);

                Debug.Log($"ASA - Cloud anchor deleted!");
            }
        }
        // </DeleteGameObject>

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
    }
}
using Microsoft.MixedReality.Toolkit.UI;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class RouteManipulator : MonoBehaviour//MonoBehaviourPunCallbacks, IInRoomCallbacks
    {
        [SerializeField] private HoldManipulator holdManipulator = default;

        // prefab to use as parent for instatiated routes
        [SerializeField] private GameObject routeParentPrefab = default;

        // scroll menu populator for route merge choices
        [SerializeField] private ScrollRouteMenuPopulator scrollRouteMenuScript = default;

        // scroll menu for route merge choices
        [SerializeField] private GameObject scrollRouteMenu = default;
        private bool showScrollRouteMenu = false;

        // filename keyboard/input related fields
        [SerializeField] private InputField keyboardInput = default;
        [SerializeField] private GameObject keyboardInputContainer = default;
        private string filename = default;
        private bool showKeyboard = false;

        // TCPClient
        [SerializeField] private TCPClient tcpClient = default;

        // room property key to access routes on server
        public static readonly string ROUTES_ON_SERVER_CUSTOM_PROPERTY = "routesOnServer";

        // enum for currently active scroll menu
        public enum ScrollMenu
        {
            None,
            Instantiate,
            Merge
        }

        // currently active scroll menu
        private ScrollMenu activeScrollMenu = ScrollMenu.None;

        private void Start()
        {
            // hide filename prompt until we want it shown
            keyboardInputContainer.SetActive(false);
        }

        /// SAVE for later reference in how to update scroll menu based on room properties
        //public override void OnEnable()
        //{
        //    base.OnEnable();
        //    PhotonNetwork.AddCallbackTarget(this);
        //}

        //public override void OnDisable()
        //{
        //    base.OnDisable();
        //    PhotonNetwork.RemoveCallbackTarget(this);
        //}

        public void InstantiateRoute(List<string> holds, List<Vector3> positions, List<Quaternion> rotations, string routeName)
        {
            // instantiate prefab to parent the holds
            Vector3 parentPosition = new Vector3(0f, 0f, 0.5f);
            GameObject routeParent = PhotonNetwork.InstantiateRoomObject(routeParentPrefab.name, parentPosition, Quaternion.identity);

            // set the name on the route parent for all clients
            // critical so that other clients can find it in their scene, i.e. in PunRPC_ReparentHoldsToRouteParent
            routeParent.GetComponent<PhotonView>().RPC("PunRPC_SetName", RpcTarget.AllBuffered, routeName);

            // instantiate the holds in the config with a temp tag
            for (int i = 0; i < holds.Count; i++)
            {
                List<string> customTags = new List<string> { routeName };
                string customTagsString = string.Join(",", customTags); // PUN2 doesn't support arrays/lists as parameters
                holdManipulator.photonView.RPC("BuildAnchor", 
                    RpcTarget.MasterClient, 
                    holds[i], 
                    positions[i], 
                    rotations[i], 
                    Vector3.one * 0.1f,
                    customTagsString);
            }

            // reparent holds from default HoldParent to new parent so as to make them easily movable altogether at once
            // Needs to be RPC here so that every client performs this reparenting, otherwise movements of child objects won't stay in-sync across clients
            // when moving a route parent on one client
            // NOTE: holds' parent is by default the HoldParent object
            // TODO: draw connecting line from hold to parent
            gameObject.GetComponent<PhotonView>().RPC("PunRPC_ReparentHoldsToRouteParent", RpcTarget.AllBuffered, routeName);
        }

        [PunRPC]
        private void PunRPC_ReparentHoldsToRouteParent(string routeName)
        {
            // reparent holds from default HoldParent to new parent so as to make them easily movable altogether at once
            // NOTE: holds' parent is by default the HoldParent object
            // TODO: draw connecting line from hold to parent
            GameObject routeParent = Array.Find(GameObject.FindGameObjectsWithTag("RouteParent"), x => x.name == routeName);
            GameObject[] holdInstances = GameObject.FindGameObjectsWithTag("Hold");
           
            for (int i = 0; i < holdInstances.Length; i++)
            {
                if (holdInstances[i].GetComponent<CustomTag>().HasTag(routeName))
                {
                   
                    holdInstances[i].transform.SetParent(routeParent.transform, true);
                }
            }
        }

        /// <summary>
        /// Reparent all holds childed under route parent to the global hold parent.  
        /// Perform a final adjustment to snap the holds to the nearest spatial mesh surface.
        /// Finally, destroy the route parent.
        /// </summary>
        /// <param name="routeParentName"></param>
        /// <param name="tag"></param>
        [PunRPC]
        public async void DoMergeRoute(string routeParentName, string tag)
        {
            // check the route parent and global hold parent are in the scene
            GameObject routeParent = Array.Find(GameObject.FindGameObjectsWithTag("RouteParent"), x => x.name == routeParentName);
            GameObject globalHoldParent = GameObject.Find("GlobalHoldParent");
            if (routeParent == null || globalHoldParent == null) return;

            // find all holds in the route of interest
            List<GameObject> holds = new List<GameObject>();
            foreach (Transform child in routeParent.transform)
            {
                if (child.tag == tag)
                {
                    holds.Add(child.gameObject);
                }
            }

            // reparent the holds to the global hold parent in the scene
            for (int i = 0; i < holds.Count; i++)
            {
                GameObject hold = holds[i];

                // set new parent but keep current position/rotation/scale
                hold.transform.SetParent(globalHoldParent.transform, true);

                // remove the identifying tag for this route so that the hold won't become parented to in the future if the same route is ever reintroduced into the scene
                hold.GetComponent<CustomTag>().RemoveTag(routeParentName);

                // movement only necessary to be done on one client i.e. master client
                if (PhotonNetwork.IsMasterClient)
                {
                    // we need owernship back for the holds before trying to move them
                    foreach (GameObject child in holds)
                    {
                        child.GetPhotonView().RequestOwnership();
                    }

                    StartCoroutine(SnapHoldToSpatialMesh(1f, hold)); // reenable after a short delay
                }
            }

            // destroy the route parent game object
            // only perform on the master client as photon will propagate the changes to the other clients
            if (PhotonNetwork.IsMasterClient)
            {
                // we need to make sure we have ownership of the route parent
                routeParent.GetPhotonView().RequestOwnership();

                // the RequestOwnership calls above are asynchronous and need time to complete before we call Destroy() below
                await Task.Delay(200);

                // destroy the route parent
                PhotonNetwork.Destroy(routeParent);
            }
        }

        /// <summary>
        /// Finds the nearest spatial mesh using both forward and backward RayCasts.  Then places hold oriented to normal at the location of the RayCast hit.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="go"></param>
        /// <returns></returns>
        IEnumerator SnapHoldToSpatialMesh(float time, GameObject go)
        {
            yield return new WaitForSeconds(time);

            float distanceForward = float.PositiveInfinity;
            float distanceBackward = float.PositiveInfinity;
            RaycastHit hitForward = new RaycastHit();
            RaycastHit hitBackward = new RaycastHit();
            Vector3 forward = go.transform.forward; // project inward, toward assumed wall position (spatial mesh)
            Vector3 backward = -1 * forward;

            // RayCast forward and backward to determine which direction is closest to spatial mesh (assumed to be wall)
            // NOTE: we turn on Physics.queriesHitBackfaces since RayCast hits aren't registered if "behind" a mesh collider (e.g. hold has clipped into a
            // wall either due to the frequent spatial mesh updates or by moving the hold via it's parent)
            if (Physics.Raycast(go.transform.position, forward, out hitForward))
            {
                distanceForward = hitForward.distance;
                //Debug.Log($"Forward distance to mesh: {distanceForward}");
            }
            if (Physics.Raycast(go.transform.position, backward, out hitBackward))
            {
                distanceBackward = hitBackward.distance;
                //Debug.Log($"Backward distance to mesh: {distanceBackward}");
            }

            //Debug.Log($"hitForward normal: {hitForward.normal}");
            //Debug.Log($"hitBackward normal: {hitBackward.normal}");

            // find placement point and rotation
            Vector3 normal = Vector3.zero;
            Quaternion normalOrientation = Quaternion.identity;
            Vector3 position = Vector3.zero;
            if (distanceForward < distanceBackward)
            {
                GetNormalOrientationAndPosition(hitForward, go, out normalOrientation, out position, out normal);
            } else
            {
                GetNormalOrientationAndPosition(hitBackward, go, out normalOrientation, out position, out normal);
            }

            //Debug.Log($"position: {position}");
            //Debug.Log($"rotation: {normalOrientation}");

            go.transform.position = position;
            go.transform.rotation = normalOrientation;
        }

        /// <summary>
        /// Get the normal orientation and position necessary to place a hold on the surface of a spatial mesh given a RayCast hit against the spatial mesh
        /// from the hold's current position
        /// </summary>
        /// <param name="hit"></param>
        /// <param name="go"></param>
        /// <param name="normalOrientation"></param>
        /// <param name="position"></param>
        /// <param name="normal"></param>
        private void GetNormalOrientationAndPosition(RaycastHit hit, GameObject go, out Quaternion normalOrientation, out Vector3 position, out Vector3 normal)
        {
            // align with normal vector of wall (spatial mesh)
            normalOrientation = Quaternion.LookRotation(-hit.normal, Vector3.up);

            // _Flipped holds have their z-axis (blue axis) point out instead of in so we reverse the raycast direction 
            if (go.name.Contains("_Flipped"))
            {
                normalOrientation = Quaternion.LookRotation(hit.normal, Vector3.up);
            }

            position = hit.point;
            normal = hit.normal;
        }

        /// <summary>
        /// Show container holding input field that triggers keyboard
        /// </summary>
        public void ToggleKeyboardInput()
        {
            if (showKeyboard)
            {
                keyboardInputContainer.SetActive(false);
                showKeyboard = false;
            }
            else
            {
                keyboardInputContainer.SetActive(true);
                showKeyboard = true;
            }
            keyboardInput.text = "";
        }

        // <ScrollRoutesMenuClick>
        /// <summary>
        /// Handle scoll route menu selection
        /// </summary>
        /// <param name="go"></param>
        public async void ScrollRouteMenuClick(GameObject go)
        {
            if (go != null)
            {
                string route = $"{go.name}";
                
                // don't process click if the route is already in scene as otherwise holds can get parented to the wrong route parent later on
                if (IsRouteInScene(route)) { return; }
               
                // retrieve route
                gameObject.GetPhotonView().RPC("GetRoute", RpcTarget.MasterClient, route);

                // empty menu and close it
                EmptyCloseScrollRouteMenu();
            }
        }

        /// <summary>
        /// Simple function to return if route parent object specified by route is in scene
        /// </summary>
        /// <param name="route"></param>
        /// <returns></returns>
        private bool IsRouteInScene(string route)
        {
            // check first if the route is already being manipulated in the scene and abort if it is so that duplicates are avoided
            GameObject[] existingRoutes = GameObject.FindGameObjectsWithTag("RouteParent");

            // search for a route parent with the name of the route
            for (int i = 0; i < existingRoutes.Length; i++)
            {
                // remove the "(Clone)" part of the object name that Unity automatically injects when instantiating prefabs
                string name = existingRoutes[i].name.Replace("(Clone)", string.Empty);
                if (name == route)
                {
                    Debug.Log($"Route: {route} already in scene");
                    return true;
                }
            }
            return false;
        }

        // <ScrollRoutesMenuClick>
        /// <summary>
        /// Handle scoll route menu selection and merge it to the main set of holds
        /// </summary>
        /// <param name="go"></param>
        public async void ScrollRouteMergeMenuClick(GameObject go)
        {
            if (go != null)
            {
                string route = $"{go.name}";

                // merge route into scene
                MergeRoute(route);

                // empty menu and close it
                // TODO: figure out how to not have to close other clients' scroll merge menus but rather update them -> may need to call GetRoutesForMerge()
                // after a long delay since MergeRoute has delays built into it
                gameObject.GetPhotonView().RPC("PunRPC_EmptyCloseScrollRouteMenu", RpcTarget.All);
            }
        }

        /// <summary>
        /// Close scroll menu for all clients
        /// </summary>
        [PunRPC]
        private void PunRPC_EmptyCloseScrollRouteMenu()
        {
            EmptyCloseScrollRouteMenu();
        }

        /// <summary>
        /// Empty and close the scroll route menu
        /// </summary>
        private void EmptyCloseScrollRouteMenu()
        {
            scrollRouteMenu.transform.Find("ScrollingObjectCollection").GetComponent<ScrollingObjectCollection>().OnClick.RemoveAllListeners();
            foreach (Transform child in scrollRouteMenu.transform.Find("ScrollingObjectCollection/Container").transform)
            {
                GameObject.Destroy(child.gameObject);
            }
            scrollRouteMenu.SetActive(false);
            showScrollRouteMenu = false;
        }

        /// <summary>
        /// Empty and close the scroll route menu
        /// </summary>
        private void EmptyScrollRouteMenu()
        {
            foreach (Transform child in scrollRouteMenu.transform.Find("ScrollingObjectCollection/Container").transform)
            {
                GameObject.Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// Merge the holds for the given route with those holds parented by the global hold parent in the scene.
        /// </summary>
        /// <param name="routeName"></param>
        /// <returns></returns>
        private async void MergeRoute(string routeName)
        {
            gameObject.GetPhotonView().RPC("DoMergeRoute", RpcTarget.All, routeName, "Hold");
        }

        /// <summary>
        /// Registered in Inspector.
        /// NOTE: need async void here: https://stackoverflow.com/questions/28601678/calling-async-method-on-button-click
        /// </summary>
        [PunRPC]
        public async void PunRPC_CreateRoute()
        {
            CreateRouteFile(filename: filename);

            // send data
            await tcpClient.SendFile(filename: filename);

            // remove the file so we don't accrue files
            DeleteRouteFile(filename: filename);
        }

        /// <summary>
        /// Called by closing keyboard in Inspector to retrieve the user's text
        /// </summary>
        /// <param name="text"></param>
        public void SaveRoute(string text)
        {
            if (text == "") 
            {
                Debug.Log("No filename given, aborting.");
                return;
            }

            filename = text + ".txt";

            // route all TCP calls through master client since it's the one with a connection
            gameObject.GetPhotonView().RPC("PunRPC_CreateRoute", RpcTarget.MasterClient);

            keyboardInputContainer.SetActive(false);
        }

        /// <summary>
        /// Creates text file with specified filename that contains information for each hold in a scene (one hold per line)
        /// </summary>
        /// <param name="filename"></param>
        private void CreateRouteFile(string filename = "")
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            using (StreamWriter sw = File.CreateText(path))
            {
                GameObject[] holds = GameObject.FindGameObjectsWithTag("Hold");

                for (int i = 0; i < holds.Length; i++)
                {
                    // compile semi-colon delimited string of form with position and rotation comma-delimited:
                    // "holdname;transform.position;transform.rotation" 
                    string info = "";

                    // name (type of hold)
                    // remove the "(Clone)" string from the name since this is added by unity at runtime
                    string name = holds[i].name;
                    name = name.Replace("(Clone)", string.Empty);
                    info += name + ";";

                    // position
                    string position = holds[i].transform.position.ToString("F9"); // get as much precision as possible
                    position = position.Trim('(').Trim(')');
                    info += position + ";";

                    // rotation
                    string rotation = holds[i].transform.rotation.ToString("F9"); // get as much precision as possible
                    rotation = rotation.Trim('(').Trim(')');
                    info += rotation;

                    sw.WriteLine(info);
                }
            }
        }

        /// <summary>
        /// Deletes file with specified filename
        /// Assumes file exists at Application.persistentDataPath
        /// </summary>
        /// <param name="filename"></param>
        private void DeleteRouteFile(string filename = "")
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.Delete(path);
        }

        /// <summary>
        /// Retrieves the saved route from the server
        /// TODO: maybe use serialization/deserialization and/or JSON?
        /// </summary>
        [PunRPC]
        private async void GetRoute(string route)
        {
            // list of hold names and their respective transforms
            List<string> holds = new List<string>();
            List<Vector3> positions = new List<Vector3>();
            List<Quaternion> rotations = new List<Quaternion>();

            (holds, positions, rotations) = await tcpClient.GetRoute(route);

            InstantiateRoute(holds, positions, rotations, route);
        }

        /// <summary>
        /// Retrieves the list of saved routes currently on the server (list of filenames)
        /// endpoint supplied inspector to choose which event listener is wired up
        /// Serves double duty between choosing routes to instantiate and choosing routes to merge
        /// TODO: maybe use serialization/deserialization and/or JSON?
        /// </summary>
        /// <param name="endpoint"></param>
        public async void GetRoutesForInstantiation()
        {
            // close/clear menu if already open so we don't accidentally keep filling it up
            if (showScrollRouteMenu)
            {
                EmptyCloseScrollRouteMenu();
                activeScrollMenu = ScrollMenu.None;
                return;
            }

            activeScrollMenu = ScrollMenu.Instantiate;

            // display scroll route menu
            scrollRouteMenu.SetActive(true);
            showScrollRouteMenu = true;

            // wire up listener on menu
            scrollRouteMenu.transform.Find("ScrollingObjectCollection").GetComponent<ScrollingObjectCollection>().OnClick.AddListener(ScrollRouteMenuClick);

            // tell master client to get the list of available routes from server and then broadcast this list to other clients
            gameObject.GetPhotonView().RPC("PunRPC_GetRouteList", RpcTarget.MasterClient);
        }

        [PunRPC]
        private async void PunRPC_GetRouteList()
        {
            // gather list of routes
            List<string> routeList = await tcpClient.GetRouteList();
            string routeListString = string.Join(",", routeList); // PUN2 doesn't support arrays/lists as parameters
            gameObject.GetPhotonView().RPC("PunRPC_UpdateScrollRouteMenu", RpcTarget.All, routeListString, (int)ScrollMenu.Instantiate);
//            // set list in room properties so other clients will update their menus
//#if UNITY_2020
//            PhotonNetwork.CurrentRoom.SetCustomProperties(
//                new ExitGames.Client.Photon.Hashtable()
//                {
//                    { ROUTES_ON_SERVER_CUSTOM_PROPERTY, routeList.ToArray() }
//                }
//            );
//#endif
        }

        /// <summary>
        /// Eventually this method should simply update the menu if open for other clients as it does for the .  But for now, due to a bug where the other clients' menus
        /// are emptied but not repopulated, we simply close/empty the menu for them.
        /// </summary>
        /// <param name="routeListString"></param>
        [PunRPC]
        private async void PunRPC_UpdateScrollRouteMenu(string routeListString, int scrollMenuType) 
        {
            // don't update the scroll menu unless we have the same one currently actively in use
            if (scrollMenuType == (int)activeScrollMenu)
            {
                // this logic allows us to empty the menu as otherwise, the split would still return on element even for empty string
                List<string> routeList = new List<string>();
                int count = 0;
                if (routeListString != "")
                {
                    routeList = routeListString.Split(',').ToList();
                    count = routeList.Count;
                } 

                // empty menu
                EmptyScrollRouteMenu();

                // emptying the menu takes a bit of time
                await Task.Delay(1000);

                // populate menu
                scrollRouteMenuScript.NumItems = count;
                scrollRouteMenuScript.MakeScrollingList(routeList);

                // necessary to call this so that scroll is enabled again: https://github.com/microsoft/MixedRealityToolkit-Unity/issues/10350
                scrollRouteMenu.transform.Find("ScrollingObjectCollection").GetComponent<ScrollingObjectCollection>().UpdateContent();
            }
        }

        //// SAVE for reference in how to update scroll menu based on room properties
        //// Eventually this method should simply update the menu if open for other clients.  But for now, due to a bug where the other clients' menus
        //// are emptied but not repopulated, we simply close/empty the menu for them.
        
        //        public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
        //        {
        //            base.OnRoomPropertiesUpdate(propertiesThatChanged);

        //            object keyValue = null;

        //#if UNITY_2020
        //            if (propertiesThatChanged.TryGetValue(
        //                ROUTES_ON_SERVER_CUSTOM_PROPERTY, out keyValue))
        //            {
        //                // If the anchorId property is present and the scroll menu is open then we will try to update the scroll menu
        //                if (keyValue != null)
        //                {
        //                    Debug.Log($"RouteManipulator:OnRoomPropertiesUpdate -> {keyValue}");
        //                    string[] routeArray = (string[])keyValue;
        //                    List<string> routeList = routeArray.ToList();
        //                    Debug.Log($"length: {routeList.Count}");
        //                    for (int i = 0; i < routeList.Count; i++)
        //                    {
        //                        Debug.Log($"room route: {routeList[i]}");
        //                    }
        //                    Debug.Log(showScrollRouteMenu);

        //                    if (showScrollRouteMenu && routeList.Count > 0)
        //                    {
        //                        Debug.Log("Emptying and Repopulating Menu");
        //                        // empty menu
        //                        EmptyScrollRouteMenu();

        //                        // populate menu
        //                        scrollRouteMenuScript.NumItems = routeList.Count;
        //                        scrollRouteMenuScript.MakeScrollingList(routeList);
        //                    }
        //                } 
        //            }
        //            else
        //            {
        //                Debug.Log("RouteManipulator:OnRoomPropertiesUpdate: routes not present in room properties");
        //            }
        //#endif
        //        }

        /// <summary>
        /// Get routes currently in scene as options to merge into the global hold parent in the scene
        /// </summary>
        public void GetRoutesForMerge()
        {
            // close/clear menu if already open so we don't accidentally keep filling it up
            if (showScrollRouteMenu)
            {
                EmptyCloseScrollRouteMenu();
                activeScrollMenu = ScrollMenu.None;
                return;
            }

            activeScrollMenu = ScrollMenu.Merge;

            // display scroll route menu
            scrollRouteMenu.SetActive(true);
            showScrollRouteMenu = true;

            // wire listener on menu
            scrollRouteMenu.transform.Find("ScrollingObjectCollection").GetComponent<ScrollingObjectCollection>().OnClick.AddListener(ScrollRouteMergeMenuClick);

            // gather list of routes
            GameObject[] routeParents = GameObject.FindGameObjectsWithTag("RouteParent");
            List<string> routeList = new List<string>();
            for (int i = 0; i < routeParents.Length; i++)
            {
                routeList.Add(routeParents[i].name);
            }

            string routeListString = string.Join(",", routeList); // PUN2 doesn't support arrays/lists as parameters
            gameObject.GetPhotonView().RPC("PunRPC_UpdateScrollRouteMenu", RpcTarget.All, routeListString, (int)ScrollMenu.Merge);
        }
    }
}
using Microsoft.MixedReality.Toolkit.Utilities.Solvers;
using MultiUserCapabilities;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class RouteManipulator : MonoBehaviour
    {
        [SerializeField] private HoldManipulator holdManipulator = default;

        // prefab to use as parent for instatiated routes
        [SerializeField] private GameObject routeParentPrefab = default;

        // scroll menu populator for route choices
        [SerializeField] private ScrollRouteMergeMenuPopulator scrollRouteMergeMenuScript = default;

        // scroll menu populator for route merge choices
        [SerializeField] private ScrollRouteMenuPopulator scrollRouteMenuScript = default;

        // scroll menu for route choices
        [SerializeField] private GameObject scrollRouteMergeMenu = default;
        private bool showScrollRouteMergeMenu = false;

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

        private void Start()
        {
            // hide filename prompt until we want it shown
            keyboardInputContainer.SetActive(false);
        }

        public void InstantiateRoute(List<string> holds, List<Vector3> positions, List<Quaternion> rotations, string routeName)
        {
            // instantiate prefab to parent the holds
            Vector3 parentPosition = new Vector3(0f, 0f, 0.5f);
            GameObject routeParent = PhotonNetwork.InstantiateRoomObject(routeParentPrefab.name, parentPosition, Quaternion.identity);
            routeParent.name = routeName;

            // update display name of hold config parent
            DisplayRouteParent(routeParent, routeName);

            // instantiate the holds in the config with a temp tag
            for (int i = 0; i < holds.Count; i++)
            {
                List<string> customTags = new List<string> { routeName };
                holdManipulator.photonView.RPC("BuildAnchor", 
                    RpcTarget.MasterClient, 
                    holds[i], 
                    positions[i], 
                    rotations[i], 
                    Vector3.one * 0.1f,
                    customTags);
            }

            // reparent holds and draw connecting line
            // reparent holds from default HoldParent to new parent so as to make them easily movable altogether at once
            // NOTE: holds' parent is by default the HoldParent object
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
        ///  Set name to be displayed on hold config parent manipulator
        /// </summary>
        /// <param name="route"></param>
        private void DisplayRouteParent(GameObject routeParent, string routeName)
        {
            TextMesh text = routeParent.transform.Find("FrameVisual/OriginText").GetComponent<TextMesh>();
            text.text = routeName;
        }

        public void ReparentHolds(string parent1, string parent2, string tag)
        {
            //GameObject newParent = GameObject.Find("HoldParent2");
            //GameObject[] holds = GameObject.FindGameObjectsWithTag("Hold");
            GameObject newParent = GameObject.Find(parent1);
            GameObject[] holds = GameObject.FindGameObjectsWithTag(tag);
            for (int i = 0; i < holds.Length; i++)
            {
                GameObject hold = holds[i];
                hold.gameObject.transform.SetParent(newParent.transform, true); // set new parent but keep current position/rotation/scale
                Debug.Log(hold.name);

                StartCoroutine(SnapHoldToSpatialMesh(1f, hold)); // reenable after a short delay
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

                // retrieve route
                await GetRoute(route);

                // empty menu and close it
                foreach (Transform child in scrollRouteMenu.transform.Find("ScrollingObjectCollection/Container").transform)
                {
                    GameObject.Destroy(child.gameObject);
                }
                scrollRouteMenu.SetActive(false);
            }
        }

        // <ScrollRoutesMenuClick>
        /// <summary>
        /// Handle scoll route menu selection and merge it to the main set of holds
        /// </summary>
        /// <param name="go"></param>
        public void ScrollRouteMergeMenuClick(GameObject go)
        {
            if (go != null)
            {
                string route = $"{go.name}";

                // merge route into broader
                MergeRoute(route);

                // empty menu and close it
                foreach (Transform child in scrollRouteMergeMenu.transform.Find("ScrollingObjectCollection/Container").transform)
                {
                    GameObject.Destroy(child.gameObject);
                }
                scrollRouteMergeMenu.SetActive(false);
            }
        }

        private void MergeRoute(string routeName)
        {
            Debug.Log($"MergeRoute: routeName: {routeName}");
        }

        /// <summary>
        /// Registered in Inspector.
        /// NOTE: need async void here: https://stackoverflow.com/questions/28601678/calling-async-method-on-button-click
        /// </summary>
        public async void CreateRoute()
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
            filename = text + ".txt";
            CreateRoute();
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
        private async Task GetRoute(string route)
        {
            // check first if the route is already being manipulated in the scene and abort if it is so that duplicates are avoided
            GameObject[] existingRoutes = GameObject.FindGameObjectsWithTag("RouteParent");
            Debug.Log(existingRoutes.Length);

            // search for a route parent with the name of the route
            for (int i = 0; i < existingRoutes.Length; i++)
            {
                // remove the "(Clone)" part of the object name that Unity automatically injects when instantiating prefabs
                string name = existingRoutes[i].name.Replace("(Clone)", string.Empty);
                if (name == route)
                {
                    Debug.Log($"Route: {route} already in scene");
                    return;
                }
            }

            // list of hold names and their respective transforms
            List<string> holds = new List<string>();
            List<Vector3> positions = new List<Vector3>();
            List<Quaternion> rotations = new List<Quaternion>();

            (holds, positions, rotations) = await tcpClient.GetRoute(route);

            // print what we got
            for (int i = 0; i < holds.Count; i++)
            {
                Debug.Log($"hold: {holds[i]}; {positions[i]}; {rotations[i]}");
            }

            InstantiateRoute(holds, positions, rotations, route);
        }

        /// <summary>
        /// Retrieves the list of saved routes currently on the server (list of filenames)
        /// TODO: maybe use serialization/deserialization and/or JSON?
        /// </summary>
        public async void GetRoutes()
        {
            // close it if already open
            if (showScrollRouteMenu)
            {
                showScrollRouteMenu = false;
                return;
            }

            // display scroll route menu
            scrollRouteMenu.SetActive(true);
            showScrollRouteMenu = true;

            List<string> routeList = await tcpClient.GetRouteList();

            // populate scroll route menu
            scrollRouteMenuScript.NumItems = routeList.Count;
            scrollRouteMenuScript.MakeScrollingList(routeList);
        }
    }
}
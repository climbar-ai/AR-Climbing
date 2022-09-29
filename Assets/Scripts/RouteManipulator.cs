using Microsoft.MixedReality.Toolkit.Utilities.Solvers;
using MultiUserCapabilities;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Scripts
{
    public class RouteManipulator : MonoBehaviour
    {
        [SerializeField] private HoldManipulator holdManipulator = default;
        [SerializeField] private GameObject routeParentPrefab = default; // prefab

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
    }
}
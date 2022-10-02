using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Initializations for the hold upon instantiation
/// </summary>
namespace MultiUserCapabilities
{
    public class HoldInit : MonoBehaviour, IPunInstantiateMagicCallback
    {
        /// <summary>
        /// Parent each created hold to the root game object so we can easily track their relative positions with respect to each other across each device running the application
        /// </summary>
        /// <param name="info"></param>
        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            GameObject root = GameObject.Find("GlobalHoldParent");
            this.transform.SetParent(root.transform, true);
        }

        /// <summary>
        /// Set custom tags for hold across all clients
        /// </summary>
        /// <param name="customTagsString"></param>
        [PunRPC]
        private void PunRPC_SetCustomTags(string customTagsString)
        {
            // set any custom tags (e.g. necessary for when instantiating hold configs)
            // NOTE: without RPC, the custom tags would only be set for this client
            List<string> customTags = customTagsString.Split(',').ToList(); // PUN2 doesn't support arrays/lists as parameters
            gameObject.GetComponent<CustomTag>().Tags = customTags;
        }
    }
}
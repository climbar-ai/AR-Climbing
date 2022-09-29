using Photon.Pun;
using UnityEngine;

/// <summary>
/// Parent each created hold to the root game object so we can easily track their relative positions with respect to each other across each device running the application
/// </summary>
namespace MultiUserCapabilities
{
    public class HoldInit : MonoBehaviour, IPunInstantiateMagicCallback
    {
        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            GameObject root = GameObject.Find("GlobalHoldParent");
            this.transform.SetParent(root.transform, true);
        }
    }
}
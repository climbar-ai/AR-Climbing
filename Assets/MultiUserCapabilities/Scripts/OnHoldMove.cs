using Photon.Pun;
using UnityEngine;

/// <summary>
/// Script to set OnHoverOver/OnHoverExit highlight colors for a photon networked GameObject
/// </summary>
namespace MultiUserCapabilities
{
    /// <summary>
    /// Script to handle showing/hiding game object when its undergoing movement with SurfaceMagnetism
    /// We hide the game object because we temporally show a ghost version of the game object while its in transit
    /// </summary>
    public class OnHoldMove : MonoBehaviour
    {
        //Get the GameObject’s mesh renderer to access the GameObject’s material and color
        MeshRenderer m_Renderer;

        //GameObject
        public GameObject m_GameObject;

        // Start is called before the first frame update
        void Start()
        {
            //Fetch the mesh renderer component from the GameObject
            m_Renderer = m_GameObject.GetComponent<MeshRenderer>();
        }

        public void OnMoveStart()
        {
            // Hide game object while we move it
            PhotonView photonView = PhotonView.Get(this);
            photonView.RPC("OnMoveBegin", RpcTarget.All);
        }

        public void OnMoveExit()
        {
            // Show game object after we are done moving it
            PhotonView photonView = PhotonView.Get(this);
            photonView.RPC("OnMoveEnd", RpcTarget.All);
        }

        [PunRPC]
        void OnMoveBegin()
        {
            this.m_Renderer.enabled = false;
        }

        [PunRPC]
        void OnMoveEnd()
        {
            this.m_Renderer.enabled = true;
        }
    }
}
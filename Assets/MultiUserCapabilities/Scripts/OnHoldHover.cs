using Photon.Pun; 
using UnityEngine;

/// <summary>
/// Script to set OnHoverOver/OnHoverExit highlight colors for a photon networked GameObject
/// </summary>
namespace MultiUserCapabilities
{
    /// <summary>
    /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnMouseOver.html
    /// </summary>
    public class OnHoldHover : MonoBehaviour
    {
        //When the pointer hovers over the GameObject, it turns to this color (red)
        Color my_MouseOverColor = Color.red;
        Color their_MouseOverColor = Color.blue;

        //This stores the GameObject’s original color
        Color m_OriginalColor;

        //Get the GameObject’s mesh renderer to access the GameObject’s material and color
        MeshRenderer m_Renderer;

        //GameObject
        public GameObject m_GameObject;

        // Start is called before the first frame update
        void Start()
        {
            //Fetch the mesh renderer component from the GameObject
            m_Renderer = m_GameObject.GetComponent<MeshRenderer>();

            //Fetch the original color of the GameObject
            m_OriginalColor = m_Renderer.material.color;
        }

        public void OnHoverOver()
        {
            // Change the color of the GameObject to red when the pointer is over GameObject
            PhotonView photonView = PhotonView.Get(this);
            photonView.RPC("OnHoverOverBegin", RpcTarget.All);
        }

        public void OnHoverExit()
        {
            // Reset the color of the GameObject back to normal
            PhotonView photonView = PhotonView.Get(this);
            photonView.RPC("OnHoverOverEnd", RpcTarget.All);
        }

        [PunRPC]
        void OnHoverOverBegin()
        {
            PhotonView photonView = PhotonView.Get(this);
            if (photonView.IsMine)
            {
                this.m_Renderer.material.color = my_MouseOverColor;
            } else
            {
                this.m_Renderer.material.color = their_MouseOverColor;
            }
        }

        [PunRPC]
        void OnHoverOverEnd()
        {
            m_Renderer.material.color = m_OriginalColor;
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiUserCapabilities
{
    /// <summary>
    /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.OnMouseOver.html
    /// </summary>
    public class OnHoldHover : MonoBehaviour
    {
        //When the pointer hovers over the GameObject, it turns to this color (red)
        Color m_MouseOverColor = Color.red;

        //This stores the GameObject’s original color
        Color m_OriginalColor;

        //Get the GameObject’s mesh renderer to access the GameObject’s material and color
        MeshRenderer m_Renderer;

        //GameObject
        public GameObject m_GameObject;

        // Start is called before the first frame update
        void Start()
        {
            Debug.Log("OnHoldHover: Start");
            Debug.Log(this);

            //Fetch the mesh renderer component from the GameObject
            m_Renderer = m_GameObject.GetComponent<MeshRenderer>();

            //Fetch the original color of the GameObject
            m_OriginalColor = m_Renderer.material.color;
        }

        public void OnHoverOver()
        {
            Debug.Log("Hover entered");
            // Change the color of the GameObject to red when the pointer is over GameObject
            m_Renderer.material.color = m_MouseOverColor;
        }

        public void OnHoverExit()
        {
            Debug.Log("Hover exited");
            // Reset the color of the GameObject back to normal
            m_Renderer.material.color = m_OriginalColor;
        }
    }
}
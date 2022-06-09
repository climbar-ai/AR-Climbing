using Microsoft.MixedReality.Toolkit.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MenuHideShow : MonoBehaviour
{
    private GameObject toggleMesh, toggleEditorMode, hideShowMenuButton;

    private bool show;

    void Start()
    {
        show = true;
        toggleMesh = GameObject.Find("ToggleMesh");
        toggleEditorMode = GameObject.Find("ToggleEditorMode");
        hideShowMenuButton = GameObject.Find("HideShowMenu");
    }

    public void hideShowMenu()
    {
        if (show)
        {
            toggleMesh.SetActive(false);
            toggleEditorMode.SetActive(false);
            show = false;

            hideShowMenuButton.GetComponent<ButtonConfigHelper>().SetQuadIconByName("IconShow");

            // move hideShowMenu button left 
            //NOTE: has drift and eventually moves the button (maybe because button position is a float?)
            //Vector3 pos = hideShowMenuButton.transform.position;
            //pos.x -= .1f;
            //hideShowMenuButton.transform.position = pos;
        }
        else
        {
            toggleMesh.SetActive(true);
            toggleEditorMode.SetActive(true);
            show = true;

            hideShowMenuButton.GetComponent<ButtonConfigHelper>().SetQuadIconByName("IconHide");

            // move hideShowMenu button right
            //NOTE: has drift and eventually moves the button (maybe because button position is a float?)
            //Vector3 pos = hideShowMenuButton.transform.position;
            //pos.x += .1f;
            //hideShowMenuButton.transform.position = pos;
        }
    }
}

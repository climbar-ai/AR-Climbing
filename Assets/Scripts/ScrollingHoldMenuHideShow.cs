using Microsoft.MixedReality.Toolkit.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ScrollingHoldMenuHideShow : MonoBehaviour
{
    public GameObject scrollingHoldMenu;

    private bool show;

    void Start()
    {
        show = true;
    }

    public void hideShowMenu()
    {
        if (show)
        {
            scrollingHoldMenu.SetActive(false);
            show = false;
        }
        else
        {
            scrollingHoldMenu.SetActive(true);
            show = true;
        }
    }
}
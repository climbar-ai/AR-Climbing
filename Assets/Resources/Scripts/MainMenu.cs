using Microsoft.MixedReality.Toolkit.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainMenu : MonoBehaviour
{
    [SerializeField] GameObject toggleDebugWindowButton = default;
    [SerializeField] GameObject debugWindow = default;

    private bool debugWindowIsActive = false;

    private void Start()
    {
        // hide debug window on start
        debugWindow.SetActive(debugWindowIsActive);
    }

    /// <summary>
    /// Hide/show debug window
    /// </summary>
    public void ToggleDebugWindow()
    {
        debugWindowIsActive = !debugWindowIsActive;
        debugWindow.SetActive(debugWindowIsActive);
        //CoreServices.DiagnosticsSystem.ShowDiagnostics = debugWindowIsActive;
        //CoreServices.DiagnosticsSystem.ShowProfiler = debugWindowIsActive;

        if (debugWindowIsActive)
        {
            toggleDebugWindowButton.GetComponent<ButtonConfigHelper>().MainLabelText = "Hide Debug";
        }
        else
        {
            toggleDebugWindowButton.GetComponent<ButtonConfigHelper>().MainLabelText = "Show Debug";
        }
    }
}

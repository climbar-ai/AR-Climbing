using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.SpatialAwareness;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class MeshControls : MonoBehaviour
{
    private bool showMesh;
    private IMixedRealitySpatialAwarenessMeshObserver meshObserver;

    // <Start>
    // Start is called before the first frame update
    void Start()
    {
        // Get the first Mesh Observer available, generally we have only one registered
        meshObserver = CoreServices.GetSpatialAwarenessSystemDataProvider<IMixedRealitySpatialAwarenessMeshObserver>();

        // disable mesh on start
        showMesh = false;
        meshObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
    }
    // </Start>

    public void ToggleMesh()
    {
        if (showMesh)
        {
            // Set to not visible
            meshObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.None;
        } else
        {
            // Set to not visible
            meshObserver.DisplayOption = SpatialAwarenessMeshDisplayOptions.Visible;
        }

        showMesh = !showMesh;
    }
}

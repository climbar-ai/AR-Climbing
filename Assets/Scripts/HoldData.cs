using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class HoldData : MonoBehaviour
{
    /// <summary>
    /// Stores the last manipulation of an object.
    /// Helps determine whether movement should correspond to rotation (i.e. manipulation script handler) or translation (i.e. surface magnetism movement handler)
    /// </summary>
    public DateTime manipulationStartTime;

    /// <summary>
    /// Holds state for object for whether it is receiving a short tap corresponding to placement
    /// Used for surface magnetism movement of the object
    /// </summary> 
    public bool isTappingToPlace = false;
}

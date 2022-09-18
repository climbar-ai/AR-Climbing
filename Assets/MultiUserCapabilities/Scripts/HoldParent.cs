using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HoldParent : MonoBehaviour
{
    [SerializeField] private AudioSource m_AudioSource;
    [SerializeField] private AudioClip onManipulationStartedAudio;
    [SerializeField] private AudioClip onManipulationEndedAudio;

    public void PlayOnManipulationStartedAudio()
    {
        m_AudioSource.PlayOneShot(onManipulationStartedAudio);
    }

    public void PlayOnManipulationEndedAudio()
    {
        m_AudioSource.PlayOneShot(onManipulationEndedAudio);
    }
}

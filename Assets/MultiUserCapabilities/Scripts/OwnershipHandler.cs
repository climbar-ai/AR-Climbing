using System;
using Microsoft.MixedReality.Toolkit.Input;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace MultiUserCapabilities
{
    [RequireComponent(typeof(PhotonView))]//, typeof(GenericNetSync))]
    public class OwnershipHandler : MonoBehaviourPun, 
        IPunOwnershipCallbacks, 
        //IMixedRealityInputHandler, 
        IMixedRealityFocusHandler
    {
        public void OnFocusEnter(FocusEventData eventData)
        {
            Debug.Log("OnFocusEnter");
            // ask the photonview for permission
            var photonView = this.GetComponent<PhotonView>();
            Debug.Log(photonView);

            photonView.RequestOwnership();
        }

        public void OnFocusExit(FocusEventData eventData)
        {
        }

        //public void OnInputDown(InputEventData eventData)
        //{
        //    photonView?.RequestOwnership();
        //}

        //public void OnInputUp(InputEventData eventData)
        //{
        //}

        public void OnOwnershipRequest(PhotonView targetView, Player requestingPlayer)
        {
            Debug.Log($"OnOwnershipRequest: ownership requested from {requestingPlayer.NickName}");
            targetView.TransferOwnership(requestingPlayer);
        }

        public void OnOwnershipTransfered(PhotonView targetView, Player previousOwner)
        {
            Debug.Log($"OnOwnershipTransfered: ownership successfully transfered from {previousOwner.NickName}");
        }

        public void OnOwnershipTransferFailed(PhotonView targetView, Player senderOfFailedRequest)
        {
            Debug.Log($"OnOwnershipTransferFailed: ownership failed transfer request from {senderOfFailedRequest.NickName}");
        }

        //private void TransferControl(Player idPlayer)
        //{
        //    if (photonView.IsMine) photonView.TransferOwnership(idPlayer);
        //}

        //private void OnTriggerEnter(Collider other)
        //{
        //    if (photonView != null) photonView.RequestOwnership();
        //}

        //private void OnTriggerExit(Collider other)
        //{
        //}

        //public void RequestOwnership()
        //{
        //    photonView.RequestOwnership();
        //}
    }
}
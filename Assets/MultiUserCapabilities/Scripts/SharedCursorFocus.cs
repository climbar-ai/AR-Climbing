using Photon.Pun;
using UnityEngine;

public class SharedCursorFocus : MonoBehaviour, IPunInstantiateMagicCallback
{
    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        PhotonView photonView = PhotonView.Get(this);
        if (photonView.IsMine)
        {
            this.gameObject.SetActive(false);
        }
    }
}

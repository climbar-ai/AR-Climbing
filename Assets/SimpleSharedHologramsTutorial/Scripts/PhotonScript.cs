using System;
using System.Threading.Tasks;
using AzureSpatialAnchors;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

public class PhotonScript : MonoBehaviourPunCallbacks
{
    enum RoomStatus
    {
        None,
        CreatedRoom,
        JoinedRoom,
        JoinedRoomDownloadedAnchor
    }

    public int emptyRoomTimeToLiveSeconds = 120;

    RoomStatus roomStatus = RoomStatus.None;

    void Start()
    {
        PhotonNetwork.ConnectUsingSettings();
    }
    public override void OnConnectedToMaster()
    {
        base.OnConnectedToMaster();

        var roomOptions = new RoomOptions();
        roomOptions.EmptyRoomTtl = this.emptyRoomTimeToLiveSeconds * 1000;
        PhotonNetwork.JoinOrCreateRoom(ROOM_NAME, roomOptions, null);
    }
    public async override void OnJoinedRoom()
    {
        base.OnJoinedRoom();

        // Note that the creator of the room also joins the room...
        if (this.roomStatus == RoomStatus.None)
        {
            this.roomStatus = RoomStatus.JoinedRoom;
        }
        await this.PopulateAnchorAsync();
    }
    public async override void OnCreatedRoom()
    {
        base.OnCreatedRoom();
        this.roomStatus = RoomStatus.CreatedRoom;
        await this.CreateAnchorAsync();
    }
    async Task CreateAnchorAsync()
    {
        // If we created the room then we will attempt to create an anchor for the parent
        // of the cubes that we are creating.
        var anchorService = this.GetComponent<AzureSpatialAnchorService>();

        var anchorId = await anchorService.CreateAnchorOnObjectAsync(this.gameObject);

        // Put this ID into a custom property so that other devices joining the
        // room can get hold of it.
#if UNITY_2020
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable()
            {
                { ANCHOR_ID_CUSTOM_PROPERTY, anchorId }
            }
        );
#endif
    }
    async Task PopulateAnchorAsync()
    {
        if (this.roomStatus == RoomStatus.JoinedRoom)
        {
            object keyValue = null;

#if UNITY_2020
            // First time around, this property may not be here so we see if is there.
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                ANCHOR_ID_CUSTOM_PROPERTY, out keyValue))
            {
                // If the anchorId property is present then we will try and get the
                // anchor but only once so change the status.
                this.roomStatus = RoomStatus.JoinedRoomDownloadedAnchor;

                // If we didn't create the room then we want to try and get the anchor
                // from the cloud and apply it.
                var anchorService = this.GetComponent<AzureSpatialAnchorService>();

                await anchorService.PopulateAnchorOnObjectAsync(
                    (string)keyValue, this.gameObject);
            }
#endif
        }
    }
    public async override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        await this.PopulateAnchorAsync();
    }
    static readonly string ANCHOR_ID_CUSTOM_PROPERTY = "anchorId";
    static readonly string ROOM_NAME = "HardCodedRoomName";
}
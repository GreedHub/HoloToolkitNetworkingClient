using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.WSA;
using System.Globalization;
using System.Threading;

public class ServerClient {
    public int connectionId;
    public string playerName;
    public GameObject playerPrefab;
    public Vector3 playerPosition;
    public Quaternion playerRotation;
}

public class PlayBox {
    public int boxId;
    public GameObject prefab;
}

public class NetworkClient : MonoBehaviour {
    [Header("Network Properties")]
    public string hostIp = "181.165.152.61";
    public int port = 3000;
    public GameObject playerPrefab;
    public GameObject otherPlayerPrefab;
    public GameObject boxToPlay;
     
    List<PlayBox> listOfBoxes = new List<PlayBox>();
    public float networkMessageSendRate = 0.05f;
    private float lastSentTime;

    private List<ServerClient> clientsList = new List<ServerClient>();
    private const int MAX_CONNECTIONS = 100;
    private int hostId;
    private int webHostId;

    private int reliableChannel;
    private int unreliableChannel;

    private int connectionId;


    private float connectionTime;
    private bool isConnected = false;
    private bool isStarted = false;
    private byte error;

    //public Text debugText;
    //public Text boxDebugText;
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private string playerName;

    private char decimalseparator = char.Parse(Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);

    public void Connect()
    {
        playerName = "player_a";

        NetworkTransport.Init();
        ConnectionConfig connectionConfig = new ConnectionConfig();

        reliableChannel = connectionConfig.AddChannel(QosType.Reliable);
        unreliableChannel = connectionConfig.AddChannel(QosType.Unreliable);

        HostTopology networkTopology = new HostTopology(connectionConfig, MAX_CONNECTIONS);

        hostId = NetworkTransport.AddHost(networkTopology, 0);
        Debug.Log("connecting to: " + hostIp);
        connectionId = NetworkTransport.Connect(hostId, hostIp, port, 0, out error);
        
        Debug.Log((NetworkError)error);

        connectionTime = Time.time;
        isConnected = true;

    }

    // Start is called before the first frame update
    void Start()
    {
        if (XRDevice.SetTrackingSpaceType(TrackingSpaceType.RoomScale))
        {
            Debug.Log("RoomScale mode was set successfully!!");
        }
        else
        {
            Debug.Log("RoomScale mode was not set successfully");
        }
        NetworkTransport.Init();
        Connect();
    }

    // Update is called once per frame
    private void FixedUpdate()
    {
        if (!isConnected)
        {
            return;
        }

        int recHostId;
        int connectionId;
        int channelId;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error;

        NetworkEventType recData = NetworkTransport.Receive(out recHostId, out connectionId, out channelId, recBuffer, bufferSize, out dataSize, out error);

        switch (recData)
        {
            case NetworkEventType.Nothing:
                break;

            case NetworkEventType.ConnectEvent:
                Debug.Log("I've connected");

                
                break;

            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                Debug.Log("Recived: " + msg);
                string[] msgArray = msg.Split('|');

                switch (msgArray[0])
                {
                    case "ASKNAME":
                        
                        for (int i = 2; i <= msgArray.Length - 1; i++)
                        {
                            string[] alreadyConnectedPlayer = msgArray[i].Split('%');
                            AddPlayer(int.Parse(alreadyConnectedPlayer[1]), alreadyConnectedPlayer[0]);
                        }
                        SendNetworkMessage("PLYRNAME|" + playerName + "|", reliableChannel, connectionId);
                        lastSentTime = Time.time;
                        break;

                    case "ADDPLAYER":
                        AddPlayer(int.Parse(msgArray[2]), msgArray[1]);
                        break;
                    
                    case "SPAWNBOX":
                        Vector3 boxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion boxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        PlayBox box = new PlayBox();
                        box.prefab = Instantiate(boxToPlay, boxPosition, boxRotation);
                        box.prefab.AddComponent<WorldAnchor>();
                        box.boxId = int.Parse(msgArray[8]);
                        box.prefab.transform.hasChanged = false;
                        listOfBoxes.Add(box);
                        break;

                    case "UPDCARTRANS":
                        
                        Vector3 updatedPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        MoveClientPlayer(clientsList.Find(x => x.connectionId == int.Parse(msgArray[8])), updatedPosition, updatedRotation);
                        break;

                    case "UPDATEBOX":

                        Vector3 updatedBoxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedBoxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        MoveBox(listOfBoxes.Find(x => x.boxId == int.Parse(msgArray[8])), updatedBoxPosition, updatedBoxRotation);
                        break;

                    case "PLAYERDC":
                        Debug.Log("Player" + msgArray[1] + " disconnected");
                        var itemToRemove = clientsList.Single(x => x.connectionId == int.Parse(msgArray[1]));
                        Destroy(itemToRemove.playerPrefab);
                        clientsList.Remove(itemToRemove);
                        break;
                }



                break;

            case NetworkEventType.DisconnectEvent:
                break;
        }
        if ((Time.time - lastSentTime) > networkMessageSendRate)
        {
            if (playerPrefab.transform.hasChanged)
            {
                UpdateServerCar();
                playerPrefab.transform.hasChanged = false;
                lastSentTime = Time.time;
            }
            foreach(PlayBox box in listOfBoxes)
            {
                if (box.prefab.transform.hasChanged)
                {
                    UpdateServerBox(box.boxId,box.prefab.transform.position,box.prefab.transform.rotation);
                    lastSentTime = Time.time;
                    box.prefab.transform.hasChanged = false;
                }
            }
        }
    }

    private void MoveBox(PlayBox box, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (box.prefab != null)
        {
            DestroyImmediate(box.prefab.GetComponent<WorldAnchor>());
            box.prefab.transform.position = updatedPosition;
            box.prefab.transform.rotation = updatedRotation;
            box.prefab.AddComponent<WorldAnchor>();
            box.prefab.transform.hasChanged = false;
        }
    }

    private float ParseFloatUnit(string value)
    {
        float parsedFloat;
        string parsedValue = value.Replace('.', decimalseparator);
        parsedValue = parsedValue.Replace(',', decimalseparator);

        if (float.TryParse(parsedValue, out parsedFloat))
        {
            return parsedFloat;
        }
        else
        {
            Debug.Log("cannot parse '" + value + "'");
            return 0;
        }

    }

    private void MoveClientPlayer(ServerClient player, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (player.playerPrefab != null)
        {
            Debug.Log(updatedPosition);
            player.playerPrefab.transform.position = updatedPosition;
            player.playerPrefab.transform.rotation = updatedRotation;
        }
    }

    private void AddPlayer(int playersConnectionId, string playersName)
    {
        //Save the player into a list
        ServerClient client = new ServerClient();
        client.connectionId = playersConnectionId;
        client.playerName = playersName;
        client.playerPrefab = Instantiate(otherPlayerPrefab, new Vector3(2, 2, 2), Quaternion.identity);
        clientsList.Add(client);
    }

    private void SendNetworkMessage(string message, int channel, int connectionId)
    {

        Debug.Log("Sending: " + message);
        byte[] msg = Encoding.Unicode.GetBytes(message);
        NetworkTransport.Send(hostId, connectionId, channel, msg, msg.Length, out error);

    }

    private void UpdateServerCar()
    {
        string msg = "UPDCARTRANS|";
        msg += playerPrefab.transform.position.x + "|";
        msg += playerPrefab.transform.position.y + "|";
        msg += playerPrefab.transform.position.z + "|";
        msg += playerPrefab.transform.rotation.x + "|";
        msg += playerPrefab.transform.rotation.y + "|";
        msg += playerPrefab.transform.rotation.z + "|";
        msg += playerPrefab.transform.rotation.w;
        //debugText.text = msg;
        SendNetworkMessage(msg, unreliableChannel, connectionId);
    }

    private void UpdateServerBox(int boxId, Vector3 position, Quaternion rotation)
    {
        string msg = "UPDATEBOX|";
        msg += position.x + "|";
        msg += position.y + "|";
        msg += position.z + "|";
        msg += rotation.x + "|";
        msg += rotation.y + "|";
        msg += rotation.z + "|";
        msg += rotation.w + "|";
        msg += boxId;
        //boxDebugText.text = msg;
        SendNetworkMessage(msg, unreliableChannel, connectionId);
    }
}


/*using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ServerClient {
    public int connectionId;
    public string playerName;
    public GameObject playerPrefab;
    public Vector3 playerPosition;
    public Quaternion playerRotation;
    public Vector3 playerUpdatedPosition;
    public Quaternion playerUpdatedRotation;
    public float timeStartedLerping;
    public bool isLerping;
}

public class PlayBox {
    public int boxId;
    public GameObject prefab;
}

public class NetworkClient : MonoBehaviour {
    [Header("Network Properties")]
    public string hostIp = "181.165.152.61";
    public int port = 3000;
    public GameObject playerPrefab;
    public GameObject otherPlayerPrefab;
    public GameObject boxToPlay;
    List<PlayBox> listOfBoxes = new List<PlayBox>();
    public float networkMessageSendRate = 0.05f;
    private float lastSentTime;
    public float timeToLerp = 0.1f;

    private List<ServerClient> clientsList = new List<ServerClient>();
    private const int MAX_CONNECTIONS = 100;
    private int hostId;
    private int webHostId;

    private int reliableChannel;
    private int unreliableChannel;

    private int connectionId;

    private float connectionTime;
    private bool isConnected = false;
    private bool isStarted = false;
    private byte error;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private string playerName;

    public void Connect()
    {
        playerName = "player_a";

        NetworkTransport.Init();
        ConnectionConfig connectionConfig = new ConnectionConfig();

        reliableChannel = connectionConfig.AddChannel(QosType.Reliable);
        unreliableChannel = connectionConfig.AddChannel(QosType.Unreliable);

        HostTopology networkTopology = new HostTopology(connectionConfig, MAX_CONNECTIONS);

        hostId = NetworkTransport.AddHost(networkTopology, 0);
        Debug.Log("connecting to: " + hostIp);
        connectionId = NetworkTransport.Connect(hostId, hostIp, port, 0, out error);

        Debug.Log(error);

        connectionTime = Time.time;
        isConnected = true;

    }

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        Connect();
    }

    // Update is called once per frame
    private void FixedUpdate()
    {
        if (!isConnected)
        {
            return;
        }

        int recHostId;
        int connectionId;
        int channelId;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error;

        NetworkEventType recData = NetworkTransport.Receive(out recHostId, out connectionId, out channelId, recBuffer, bufferSize, out dataSize, out error);

        switch (recData)
        {
            case NetworkEventType.Nothing:
                break;

            case NetworkEventType.ConnectEvent:
                Debug.Log("I've connected");
                break;

            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                Debug.Log("Recived: " + msg);
                string[] msgArray = msg.Split('|');

                switch (msgArray[0])
                {
                    case "ASKNAME":
                        for (int i = 2; i <= msgArray.Length - 1; i++)
                        {
                            string[] alreadyConnectedPlayer = msgArray[i].Split('%');
                            AddPlayer(int.Parse(alreadyConnectedPlayer[1]), alreadyConnectedPlayer[0]);
                        }
                        SendNetworkMessage("PLYRNAME|" + playerName + "|", reliableChannel, connectionId);
                        lastSentTime = Time.time;
                        break;

                    case "ADDPLAYER":
                        AddPlayer(int.Parse(msgArray[2]), msgArray[1]);
                        break;
                    
                    case "SPAWNBOX":
                        Vector3 boxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion boxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        PlayBox box = new PlayBox();
                        box.prefab = Instantiate(boxToPlay, boxPosition, boxRotation);
                        box.boxId = int.Parse(msgArray[8]);
                        listOfBoxes.Add(box);
                        break;

                    case "UPDCARTRANS":
                        Vector3 updatedPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        MoveClientPlayer(clientsList.Find(x => x.connectionId == int.Parse(msgArray[8])), updatedPosition, updatedRotation);
                        break;

                    case "UPDATEBOX":

                        Vector3 updatedBoxPosition = new Vector3(ParseFloatUnit(msgArray[1]), ParseFloatUnit(msgArray[2]), ParseFloatUnit(msgArray[3]));
                        Quaternion updatedBoxRotation = new Quaternion(ParseFloatUnit(msgArray[4]), ParseFloatUnit(msgArray[5]), ParseFloatUnit(msgArray[6]), ParseFloatUnit(msgArray[7]));
                        MoveBox(listOfBoxes.Find(x => x.boxId == int.Parse(msgArray[8])), updatedBoxPosition, updatedBoxRotation);
                        break;

                    case "PLAYERDC":
                        Debug.Log("Player" + msgArray[1] + " disconnected");
                        var itemToRemove = clientsList.Single(x => x.connectionId == int.Parse(msgArray[1]));
                        Destroy(itemToRemove.playerPrefab);
                        clientsList.Remove(itemToRemove);
                        break;
                }



                break;

            case NetworkEventType.DisconnectEvent:
                break;
        }
        if ((Time.time - lastSentTime) > networkMessageSendRate)
        {
            if (playerPrefab.transform.hasChanged)
            {
                UpdateServerCar();
                playerPrefab.transform.hasChanged = false;
                lastSentTime = Time.time;
            }
            foreach(PlayBox box in listOfBoxes)
            {
                if (box.prefab.transform.hasChanged)
                {
                    UpdateServerBox(box.boxId,box.prefab.transform.position,box.prefab.transform.rotation);
                    lastSentTime = Time.time;
                    box.prefab.transform.hasChanged = false;
                }
            }
        }

        foreach(ServerClient player in clientsList)
        {
            if (!player.isLerping)
            {
                player.playerPosition = player.playerPrefab.transform.position;
                player.playerRotation = player.playerPrefab.transform.rotation;
                player.timeStartedLerping = Time.time;
                player.isLerping = true;
            }

            float lerpPercent = (Time.time - player.timeStartedLerping) / timeToLerp;
            Debug.Log(lerpPercent + "|timeStartedLerping: " + player.timeStartedLerping + "|timeToLerp: " + timeToLerp);

            player.playerPrefab.transform.position = Vector3.Lerp(player.playerPosition, player.playerUpdatedPosition, lerpPercent);
            player.playerPrefab.transform.rotation = Quaternion.Lerp(player.playerRotation, player.playerUpdatedRotation, lerpPercent);

            if (lerpPercent >= 1)
            {
                player.isLerping = false;
            }
        }
    }

    private void MoveBox(PlayBox box, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (box.prefab != null)
        {
            box.prefab.transform.position = updatedPosition;
            box.prefab.transform.rotation = updatedRotation;
            box.prefab.transform.hasChanged = false;
        }
    }

    private float ParseFloatUnit(string value)
    {
        float parsedFloat;

        if (float.TryParse(value, out parsedFloat))
        {
            return parsedFloat;
        }
        else
        {
            Debug.Log("cannot parse '" + value + "'");
            return 0;
        }

    }

    private void MoveClientPlayer(ServerClient player, Vector3 updatedPosition, Quaternion updatedRotation)
    {
        if (player.playerPrefab != null)
        {
            player.playerUpdatedPosition = updatedPosition;
            player.playerUpdatedRotation = updatedRotation;           
        }
    }

    private void AddPlayer(int playersConnectionId, string playersName)
    {
        //Save the player into a list
        ServerClient client = new ServerClient();
        client.connectionId = playersConnectionId;
        client.playerName = playersName;
        client.isLerping = false;
        client.playerPrefab = Instantiate(otherPlayerPrefab, new Vector3(2, 2, 2), Quaternion.identity);
        clientsList.Add(client);
    }

    private void SendNetworkMessage(string message, int channel, int connectionId)
    {

        Debug.Log("Sending: " + message);
        byte[] msg = Encoding.Unicode.GetBytes(message);
        NetworkTransport.Send(hostId, connectionId, channel, msg, msg.Length, out error);

    }

    private void UpdateServerCar()
    {
        string msg = "UPDCARTRANS|";
        msg += playerPrefab.transform.position.x + "|";
        msg += playerPrefab.transform.position.y + "|";
        msg += playerPrefab.transform.position.z + "|";
        msg += playerPrefab.transform.rotation.x + "|";
        msg += playerPrefab.transform.rotation.y + "|";
        msg += playerPrefab.transform.rotation.z + "|";
        msg += playerPrefab.transform.rotation.w;

        SendNetworkMessage(msg, unreliableChannel, connectionId);
    }

    private void UpdateServerBox(int boxId, Vector3 position, Quaternion rotation)
    {
        string msg = "UPDATEBOX|";
        msg += position.x + "|";
        msg += position.y + "|";
        msg += position.z + "|";
        msg += rotation.x + "|";
        msg += rotation.y + "|";
        msg += rotation.z + "|";
        msg += rotation.w + "|";
        msg += boxId;

        SendNetworkMessage(msg, unreliableChannel, connectionId);
    }
}
*/
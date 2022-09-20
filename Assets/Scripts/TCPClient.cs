using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

#if !UNITY_EDITOR
using System.Threading.Tasks;
#endif


public class TCPClient : MonoBehaviour
{
    [SerializeField] private GameObject holdsParent;

#if !UNITY_EDITOR
    private bool _useUWP = true;
    private Windows.Networking.Sockets.StreamSocket socket;
    private Task exchangeTask;
#endif

#if UNITY_EDITOR
    private bool _useUWP = false;
    System.Net.Sockets.TcpClient client;
    System.Net.Sockets.NetworkStream stream;
    private Thread exchangeThread;
#endif

    //private Byte[] bytes = new Byte[256];
    private StreamWriter writer;
    private StreamReader reader;

    private int BUFFER_SIZE = 1024;

    public void Start()
    {
        //Server ip address and port
        Connect("10.203.94.234", "8081");
    }

    public void Connect(string host, string port)
    {
        if (_useUWP)
        {
            ConnectUWP(host, port);
        }
        else
        {
            ConnectUnity(host, port);
        }
    }

#if UNITY_EDITOR
    private void ConnectUWP(string host, string port)
#else
    private async void ConnectUWP(string host, string port)
#endif
    {
#if UNITY_EDITOR
        errorStatus = "UWP TCP client used in Unity!";
#else
        try
        {
            if (exchangeTask != null) CloseConnection();

            socket = new Windows.Networking.Sockets.StreamSocket();
            Windows.Networking.HostName serverHost = new Windows.Networking.HostName(host);
            await socket.ConnectAsync(serverHost, port);

            Stream streamOut = socket.OutputStream.AsStreamForWrite();
            writer = new StreamWriter(streamOut) { AutoFlush = true };

            Stream streamIn = socket.InputStream.AsStreamForRead();
            reader = new StreamReader(streamIn);

            successStatus = "Connected!";
        }
        catch (Exception e)
        {
            errorStatus = e.ToString();
        }
#endif
    }

    private void ConnectUnity(string host, string port)
    {
#if !UNITY_EDITOR
        errorStatus = "Unity TCP client used in UWP!";
#else
        try
        {
            if (exchangeThread != null) CloseConnection();

            client = new System.Net.Sockets.TcpClient(host, Int32.Parse(port));
            stream = client.GetStream();
            reader = new StreamReader(stream);
            writer = new StreamWriter(stream) { AutoFlush = true };

            successStatus = "Connected!";
        }
        catch (Exception e)
        {
            errorStatus = e.ToString();
        }
#endif
    }

    private bool exchanging = false;
    private bool exchangeStopRequested = false;
    private string lastPacket = null;

    private string errorStatus = null;
    private string successStatus = null;

    public void Update()
    {
        if (errorStatus != null)
        {
            Debug.Log(errorStatus);
            errorStatus = null;
        }
        if (successStatus != null)
        {
            Debug.Log(successStatus);
            successStatus = null;
        }
    } 

    public void SendFile(string filename="")
    {
        try
        {
            char[] response = new char[BUFFER_SIZE];

            // notify server of endpoint to use
            writer.Write("receiveFile");

            // get receipt confirmation
            reader.Read(response, 0, response.Length);
            Debug.Log($"response: {response}");
            if (response.Length <= 0 || !response.Equals("received")) { return; }

            // send filename
            writer.Write(filename);

            // get receipt confirmation
            reader.Read(response, 0, response.Length);
            Debug.Log($"response: {response}");
            if (response.Length <= 0 || !response.Equals("received")) { return; }

            // send file contents
            string path = Path.Combine(Application.persistentDataPath, filename);
            using (TextReader sr = File.OpenText(path))
            {
                string s = "";
                while ((s = sr.ReadLine()) != null)
                {
                    // send line of file to server
                    Debug.Log($"Tx: {s}");
                    writer.Write(s);

                    // get receipt confirmation
                    reader.Read(response, 0, response.Length);
                    Debug.Log($"response: {response}");
                    if (response.Length <= 0 || !response.Equals("received")) { return; }
                }
                Debug.Log("Tx: Finished");
                //writer.Write("done");
            }
        }
        catch (Exception e)
        {
            Debug.Log(e.ToString());
        }
    }


    public void CloseConnection()
    {
        exchangeStopRequested = true;

#if UNITY_EDITOR
        if (exchangeThread != null)
        {
            exchangeThread.Abort();
            stream.Close();
            client.Close();
            writer.Close();
            reader.Close();

            stream = null;
            exchangeThread = null;
        }
#else
        if (exchangeTask != null)
        {
            exchangeTask.Wait();
            socket.Dispose();
            writer.Dispose();
            reader.Dispose();

            socket = null;
            exchangeTask = null;
        }
#endif
        writer = null;
        reader = null;
    }

    public void OnDestroy()
    {
        CloseConnection();
    }

    public void SaveHolds()
    {
        string filename = "holds.txt";
        CreateFile(filename: filename);

        // send data
        SendFile(filename: filename);

        // close connection so we can later reconnect
        CloseConnection();

        // remove the file so we don't accrue files
        DeleteFile(filename: filename);
    }

    /// <summary>
    /// Creates file with specified filename
    /// </summary>
    /// <param name="filename"></param>
    private void CreateFile(string filename="")
    {
        string path = Path.Combine(Application.persistentDataPath, filename);
        using (StreamWriter sw = File.CreateText(path))
        {
            sw.WriteLine($"Time: {Time.time}");
            sw.WriteLine("Hello World");
        }
    }

    /// <summary>
    /// Deletes file with specified filename
    /// Assumes file exists at Application.persistentDataPath
    /// </summary>
    /// <param name="filename"></param>
    private void DeleteFile(string filename="")
    {
        string path = Path.Combine(Application.persistentDataPath, filename);
        File.Delete(path);
    }
}

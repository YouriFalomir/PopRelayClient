﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class JsonAndData
{
	public string Json;
	public byte[] Data;
};


public class PopRelayCacheWriter : MonoBehaviour
{
	[InspectorButton("ClearCache")]
	public bool				_Clear_Cache;

	public PopRelayClient	_Client;
	public PopRelayClient	Client { get { return (_Client != null) ? _Client : GetComponent<PopRelayClient>(); } }
	public PopRelayDecoder	ClientDecoder { get { return Client.GetComponent<PopRelayDecoder>();}}

	public bool				ClearOnFirstWrite = false;
	bool					FirstWrite = true;

	public TextAsset		Cache;
#if UNITY_EDITOR
	public string			Filename { get { return AssetDatabase.GetAssetPath(Cache); } }
#else
	public string			Filename	{	get{	return null;	}}
#endif

	List<JsonAndData>		WriteQueue_JsonAndData;
	List<string>			WriteQueue_String;
	List<byte[]>			WriteQueue_Bytes;
	List<PopMessageBinary>	WriteQueue_Binary;
	System.Threading.WaitCallback		ExecuteThread;

	[Range(0,1000)]
	public int				WritesPerFrame = 1;

	public bool				WriteOnlyText = true;

	[ShowFunctionResult("GetQueueSize",10)]
	public bool				Something = true;

	public int				GetQueueSize()
	{
		Pop.AllocIfNull(ref WriteQueue_JsonAndData);
		Pop.AllocIfNull(ref WriteQueue_String);
		Pop.AllocIfNull(ref WriteQueue_Bytes);
		Pop.AllocIfNull(ref WriteQueue_Binary);
		return WriteQueue_String.Count + WriteQueue_Bytes.Count + WriteQueue_Binary.Count;
	}

	void PopQueue<T>(ref List<T> ItemQueue,System.Action<T> RunItem)
	{
		Pop.AllocIfNull( ref ItemQueue );
		if ( ItemQueue.Count == 0 )
			return;

		//	pop header of queue
		T Item = default(T);
		lock( ItemQueue )
		{
			Item = ItemQueue[0];
			ItemQueue.RemoveAt (0);
		}
			
		RunItem.Invoke (Item);			
	}

	void EncodeAndQueue(PopMessageBinary Packet)
	{
		if (WriteOnlyText)
		{
			var Encoded = EncodeToText(Packet);
			QueueWrite(Encoded);
		}
		else
		{
			QueueWrite(Packet.Data);
		}
	}

	void EncodeAndQueue(JsonAndData Packet)
	{
		//	if data is RGBA encode to jpeg!
		var PacketMeta = JsonUtility.FromJson<ImagePacket>(Packet.Json);
		if ( PacketMeta.Encoding == PopRelayEncoding.Type.Rgba )
		{
			try
			{
				var Texture = new Texture2D(PacketMeta.Width, PacketMeta.Height, TextureFormat.RGBA32, false);
				Texture.LoadRawTextureData(Packet.Data);
				Packet.Data = Texture.EncodeToJPG(99);
				PopX.Json.Replace( ref Packet.Json, "Encoding", PopRelayEncoding.Type.Jpeg );
			}
			catch(System.Exception e)
			{
				Debug.LogException(e);
				return;
			}
		}

		WriteToFile(Packet);
	}

	void ProcessQueues()
	{
		PopQueue(ref WriteQueue_JsonAndData, EncodeAndQueue);
		PopQueue (ref WriteQueue_Binary, EncodeAndQueue);
		PopQueue(ref WriteQueue_Bytes, WriteToFile);
		PopQueue(ref WriteQueue_String, WriteToFile);
	}

	void QueueWrite(string Json,byte[] Data,PopRelayEncoding Encoding)
	{
		var JsonAndDataStruct = new JsonAndData();
		JsonAndDataStruct.Json = Json;
		JsonAndDataStruct.Data = Data;

		Pop.AllocIfNull(ref WriteQueue_JsonAndData);
		lock (WriteQueue_Binary)
		{
			WriteQueue_JsonAndData.Add(JsonAndDataStruct);
		};
		OnQueueChanged();
	}

	public void QueueWrite(PopMessageBinary Packet)
	{
		Pop.AllocIfNull (ref WriteQueue_Binary);
		lock (WriteQueue_Binary) {
			WriteQueue_Binary.Add (Packet);
		};
		OnQueueChanged ();
	}

	public void QueueWrite(PopMessageText Packet)
	{
		QueueWrite(Packet.Data);
	}

	public void QueueWrite(string Packet)
	{
		Pop.AllocIfNull (ref WriteQueue_String);
		lock (WriteQueue_String) {
			WriteQueue_String.Add (Packet);
		};
		OnQueueChanged ();
	}


	public void QueueWrite(byte[] Packet)
	{
		Pop.AllocIfNull(ref WriteQueue_Bytes);
		lock (WriteQueue_Bytes)
		{
			WriteQueue_Bytes.Add(Packet);
		};
		OnQueueChanged();
	}

	public void QueueWrite<T>(T Packet)
	{
		var Json = JsonUtility.ToJson(Packet);
		QueueWrite(Json);
	}


	void OnQueueChanged()
	{
#if UNITY_EDITOR
		EditorUtility.SetDirty (this);
#endif
		/*
		if ( ExecuteThread != null )
			return;

		ExecuteThread = (State) => {
			ExecuteQueue ();
			ExecuteThread = null;
		};
		System.Threading.ThreadPool.QueueUserWorkItem (ExecuteThread);
		*/
	}


	void OnEnable()
	{
		FirstWrite = true;
		ClientDecoder.OnDecodedPacket.AddListener (QueueWrite);
	}

	void OnDisable()
	{
		ClientDecoder.OnDecodedPacket.RemoveListener (QueueWrite);
	}

	void Update()
	{
		for ( int i=0;	i<WritesPerFrame;	i++ )
		{
			ProcessQueues ();
		}
	}

	public void ClearCache()
	{
		System.IO.File.WriteAllText( Filename, "" );
	}

	void WriteToFile(JsonAndData Packet)
	{
		WriteToFile(Packet.Json);
		WriteToFile(Packet.Data);
	}

	void WriteToFile(string Packet)
	{
		if ( FirstWrite && ClearOnFirstWrite )
		{
			ClearCache();
			FirstWrite = false;
		}
		
		var OpeningBrace = '{';
		var ClosingBrace = '}';

		//	do some json verication 
		if (Packet[0] != OpeningBrace)
			throw new System.Exception("Expecting JSON opening brace for cache");
		if (Packet[Packet.Length-1] != ClosingBrace)
			throw new System.Exception("Expecting JSON closing brace for cache");
	
		System.IO.File.AppendAllText(Filename, Packet);

		//	for readability
		if ( WriteOnlyText )
			System.IO.File.AppendAllText(Filename, "\n\n\n");
	}

	void WriteToFile(byte[] Packet)
	{
		if (FirstWrite && ClearOnFirstWrite)
		{
			ClearCache();
			FirstWrite = false;
		}

		//	gr: should still have some json I think...
		/*
		var OpeningBrace = '{';
		var ClosingBrace = '}';

		//	do some json verication 
		if (Packet[0] != OpeningBrace)
			throw new System.Exception("Expecting JSON opening brace for cache");
		if (Packet[Packet.Length - 1] != ClosingBrace)
			throw new System.Exception("Expecting JSON closing brace for cache");
*/
		//	https://stackoverflow.com/a/6862460/355753
		using (var stream = new System.IO.FileStream(Filename, System.IO.FileMode.Append))
		{
			stream.Write(Packet, 0, Packet.Length);
		}
/*
		//	for readability
		System.IO.File.AppendAllText(Filename, "\n\n\n");
		*/
	}

	string EncodeToText(PopMessageBinary Packet)
	{
		//	see if we need to grab the data from the tail of the packet and re-encode it
		var JsonLength = PopX.Json.GetJsonLength(Packet.Data);

		var TailDataSize = Packet.Data.Length - JsonLength;
		Debug.Log("Binary packet has " + TailDataSize + " data");

		//	all json, just treat it as text
		if (TailDataSize == 0)
		{
			var DataString = System.Text.Encoding.UTF8.GetString(Packet.Data);
			return DataString;
		}

		//	grab tail data, encode to base 64.
		//	update encoding and inject into json
		string Json;
		byte[] Data;
		PopRelayEncoding Encoding;
		PopRelayDecoder.DecodePacket( Packet, out Json, out Data, out Encoding );

		//	encode to base 64
		var Data64 = System.Convert.ToBase64String (Data);
		Encoding.Push(PopRelayEncoding.Type.Base64);
		PopX.Json.Replace (ref Json, "Encoding", Encoding.GetString());
		PopX.Json.Append (ref Json, "Data", Data64);
		return Json;
	}

}

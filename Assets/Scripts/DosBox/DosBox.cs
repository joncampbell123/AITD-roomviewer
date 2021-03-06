﻿using System;
using UnityEngine;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UI;

public class DosBox : MonoBehaviour
{
	public Text RightText;
	public BoxInfo BoxInfo;
	public GameObject Actors;
	public Arrow Arrow;
	public Box BoxPrefab;
	public Box[] Boxes;
	public uint InternalTimer1;
	public bool ShowAdditionalInfo;
	public bool ShowAITD1Vars;
	public bool IsCDROMVersion;
	public bool SpeedRunMode;

	public ProcessMemoryReader ProcessReader;
	public Box Player;

	//initial player position
	private int dosBoxPattern;
	private byte[][] PlayerInitialPosition = new byte[][] //0xFF will match any byte value (wildcard)
	{
		new byte[] { 0x9F, 0x0C, 0x00, 0x00, 0xF4, 0xF9, 0x9F, 0x0C, 0x00, 0x00, 0xF4, 0xF9 }, //AITD1
		new byte[] { 0x43, 0x01, 0x00, 0x00, 0xD0, 0xE4, 0x43, 0x01, 0x00, 0x00, 0xD0, 0xE4 }, //AIID2
		new byte[] { 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00 }, //AITD3
		new byte[] { 0xFF, 0x09, 0x00, 0x00, 0xFF, 0xF6, 0xFF, 0x09, 0x00, 0x00, 0xFF, 0xF6, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 }  //JITD
	};

	private byte[] objectMemoryPattern = new byte[] { 0x61, 0x00, 0x02, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF };

	private int[] playerInitialSlot = new [] { 1, 0, 0, 6 };
	//offset to apply to get beginning of actors array
	private int[] actorStructSize = new [] { 160, 180, 182, 180 };
	//size of one actor
	private int[] trackModeOffsets = new [] { 82, 90, 90, 90 };

	private Vector3 lastPlayerPosition;
	private int lastValidPlayerIndex = -1;
	private int linkfloor = 0;
	private int linkroom = 0;
	private byte[] memory = new byte[16384];

	//fps
	private int oldFramesCount;
	private Queue<int> previousFramesCount = new Queue<int>();
	private Queue<float> previousFrameTime = new Queue<float>();

	private float lastDelay;
	private Timer delayCounter = new Timer();
	private Timer totalDelay = new Timer();

	private int inHand;
	private bool allowInventory;
	private bool saveTimerFlag;
	private ushort internalTimer2;
	private int targetSlot;

	public void Start()
	{
		//game has maximum 50 actors
		Boxes = new Box[50];
		for (int i = 0; i < Boxes.Length; i++)
		{
			Box box = Instantiate(BoxPrefab);
			box.transform.parent = Actors.transform;
			box.name = "Actor";
			box.Slot = i;
			box.DosBox = this;
			Boxes[i] = box;
		}
	}

	void OnDestroy()
	{
		if (ProcessReader != null)
		{
			ProcessReader.Close();
		}
	}

	public void UpdateAllActors()
	{
		Player = null;
		if (ProcessReader != null)
		{
			if (ProcessReader.Read(memory, Shared.ActorsMemoryAdress, memory.Length) > 0)
			{
				//read actors info
				int i = 0;
				foreach (Box box in Boxes)
				{
					int k = i * actorStructSize[dosBoxPattern];
					box.ID = memory.ReadShort(k + 0);

					if (box.ID != -1)
					{
						int trackModeOffset = trackModeOffsets[dosBoxPattern];
						box.Body = memory.ReadShort(k + 2);
						box.Flags = memory.ReadShort(k + 4);
						box.ColFlags = memory.ReadShort(k + 6);

						memory.ReadBoundingBox(k + 8, out box.BoundingLower, out box.BoundingUpper);

						FixBoundingWrap(ref box.BoundingLower.x, ref box.BoundingUpper.x);
						FixBoundingWrap(ref box.BoundingLower.z, ref box.BoundingUpper.z);

						memory.ReadBoundingBox(k + 20, out box.Box2DLower, out box.Box2DUpper);

						box.LocalPosition = memory.ReadVector(k + 28);
						box.WorldPosition = memory.ReadVector(k + 34);
						box.Angles = memory.ReadVector(k + 40);

						box.Floor = memory.ReadShort(k + 46);
						box.Room = memory.ReadShort(k + 48);
						box.LifeMode = memory. ReadShort(k + 50);
						box.Life = memory.ReadShort(k + 52);
						box.Chrono = memory.ReadUnsignedInt(k + 54);
						box.RoomChrono = memory.ReadUnsignedInt(k + 58);
						box.Anim = memory.ReadShort(k + 62);
						box.AnimType = memory.ReadShort(k + 64);
						box.NextAnim = memory.ReadShort(k + 66);
						box.Keyframe = memory.ReadShort(k + 74);
						box.TotalFrames = memory.ReadShort(k + 76);
						box.EndFrame = memory.ReadShort(k + 78);
						box.EndAnim = memory.ReadShort(k + 80);

						box.TrackMode = memory.ReadShort(k + trackModeOffset);
						box.TrackNumber = memory.ReadShort(k + 84);
						box.PositionInTrack = memory.ReadShort(k + 88);

						if(dosBoxPattern == 0) //AITD1 only
						{
							box.Mod = memory.ReadVector(k + 90);
						}
						else
						{
							box.Mod = Vector3.zero;
						}

						box.OldAngle = memory.ReadShort(k + 106);
						box.NewAngle = memory.ReadShort(k + 108);
						box.RotateTime = memory.ReadShort(k + 110);
						box.Speed = memory.ReadShort(k + 116);

						box.Col = memory.ReadVector(k + 126);
						box.ColBy = memory.ReadShort(k + 132);
						box.HardTrigger = memory.ReadShort(k + 134);
						box.HardCol = memory.ReadShort(k + 136);
						box.Hit = memory.ReadShort(k + 138);
						box.HitBy = memory.ReadShort(k + 140);
						box.ActionType = memory.ReadShort(k + 142);
						box.HotBoxSize = memory.ReadShort(k + 148);
						box.HitForce = memory.ReadShort(k + 150);
						box.HotPosition = memory.ReadVector(k + 154);
					}

					i++;
				}

				//find player + switch floor if necessary
				foreach (Box box in Boxes)
				{
					bool isActive = box.ID != -1;
					if (isActive)
					{
						//player
						if (box.TrackMode == 1 || box.ID == lastValidPlayerIndex)
						{
							//update player index
							lastValidPlayerIndex = box.ID;

							//automatically switch room and floor (has to be done before setting other actors positions)
							if (linkfloor != box.Floor || linkroom != box.Room)
							{
								linkfloor = box.Floor;
								linkroom = box.Room;

								GetComponent<RoomLoader>().RefreshRooms(linkfloor, linkroom);
							}
						}
					}
				}

				//update all boxes
				foreach (Box box in Boxes)
				{
					if (box.ID != -1)
					{
						Transform roomObject = GetComponent<RoomLoader>().GetRoom(box.Floor, box.Room);
						if (roomObject != null)
						{
							//local to global position
							Vector3 boxPosition = box.BoundingPos / 1000.0f;
							boxPosition = new Vector3(boxPosition.x, -boxPosition.y, boxPosition.z) + roomObject.localPosition;

							if (box.transform.position != boxPosition)
							{
								Vector3 offset = 1000.0f * (box.transform.position - boxPosition);
								float distance = new Vector3(Mathf.Round(offset.x), 0.0f, Mathf.Round(offset.z)).magnitude;
								box.LastOffset = Mathf.RoundToInt(distance);
								box.LastDistance += distance;
								box.transform.position = boxPosition;
							}

							//make actors appears slightly bigger than they are to be not covered by colliders
							Vector3 delta = Vector3.one;
							box.transform.localScale = (box.BoundingSize + delta) / 1000.0f;

							//make sure very small actors are visible
							box.transform.localScale = Vector3.Max(box.transform.localScale, Vector3.one * 0.1f);

							bool isAITD1 = dosBoxPattern == 0;
							if (isAITD1)
							{
								UpdateHotPointBox(box, roomObject.localPosition);
							}

							if (ShowAITD1Vars)
							{
								if(box.PreviousAnim != box.Anim || box.PreviousKeyFrame != box.Keyframe || box.EndFrame == 1 || box.EndAnim == 1)
								{
									box.PreviousAnim = box.Anim;
									box.PreviousKeyFrame = box.Keyframe;
									box.lastKeyFrameChange.Reset();
								}

								if (saveTimerFlag)
								{
									box.lastKeyFrameChange.Stop();
								}
								else
								{
									box.lastKeyFrameChange.Start();
								}
							}

							//player
							bool isPlayer = box.ID == lastValidPlayerIndex;
							if (isPlayer)
							{
								//check if player has moved
								if (box.transform.position != lastPlayerPosition)
								{
									//center camera to player position
									GetComponent<RoomLoader>().CenterCamera(new Vector2(box.transform.position.x, box.transform.position.z));
									lastPlayerPosition = box.transform.position;
								}

								//follow player
								Arrow.transform.position = box.transform.position + new Vector3(0.0f, box.transform.localScale.y / 2.0f + 0.001f, 0.0f);

								//face camera
								float angle = box.Angles.y * 360.0f / 1024.0f;
								Arrow.transform.rotation = Quaternion.AngleAxis(90.0f, -Vector3.left);
								Arrow.transform.rotation *= Quaternion.AngleAxis((angle + 180.0f) % 360.0f, Vector3.forward);

								float minBoxScale = Mathf.Min(box.transform.localScale.x, box.transform.localScale.z);
								Arrow.transform.localScale = new Vector3(
									minBoxScale * 0.9f,
									minBoxScale * 0.9f,
									1.0f);

								//player is white
								box.Color = new Color32(255, 255, 255, 255);
								Arrow.AlwaysOnTop = Camera.main.orthographic;
								Player = box;
							}
							else
							{
								if (box.Slot == 0)
								{
									box.Color = new Color32(255, 255, 255, 255);
								}
								else
								{
									//other actors are green
									box.Color = new Color32(0, 128, 0, 255);
								}
							}

							if (isAITD1)
							{
								UpdateWorldPosBox(box, roomObject.localPosition, isPlayer);
							}

							box.AlwaysOnTop = Camera.main.orthographic;
							box.gameObject.SetActive(true);
						}
						else
						{
							box.gameObject.SetActive(false);
						}
					}
					else
					{
						box.gameObject.SetActive(false);
					}
				}

				if (ShowAITD1Vars)
				{
					//internal timer
					ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6, 4);
					InternalTimer1 = memory.ReadUnsignedInt(0);

					//internal timer 2
					ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6 + 0xA5CE, 2);
					internalTimer2 = memory.ReadUnsignedShort(0);

					//inventory
					ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6 - 0x1A4, 2);
					allowInventory = memory.ReadShort(0) == 1;

					//inhand
					ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 + 0xA33C, 2);
					inHand = memory.ReadShort(0);

					//set by AITD when long running code is started (eg: loading ressource)
					ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6 + 0x13EA, 4);
					saveTimerFlag = memory[0] == 1;
				}
			}
			else
			{
				//unlink DOSBOX
				GetComponent<RoomLoader>().ProcessKey(KeyCode.L);
			}
		}

		//arrow is only active if actors are active and player is active
		Arrow.gameObject.SetActive(Actors.activeSelf
			&& Player != null
			&& Player.gameObject.activeSelf
			&& Player.transform.localScale.magnitude > 0.01f);
	}

	void UpdateHotPointBox(Box box, Vector3 roomPosition)
	{
		//hot point
		Box hotPoint = box.BoxHotPoint;

		if (box.ActionType == 2)
		{
			if (hotPoint == null)
			{
				hotPoint = Instantiate(BoxPrefab);
				hotPoint.name = "HotPoint";
				hotPoint.Color = new Color32(255, 0, 0, 255);
				Destroy(hotPoint.gameObject.GetComponent<BoxCollider>());
				box.BoxHotPoint = hotPoint;
			}

			Vector3 finalPos = (box.HotPosition + box.LocalPosition + box.Mod) / 1000.0f;
			finalPos = new Vector3(finalPos.x, -finalPos.y, finalPos.z) + roomPosition;
			hotPoint.transform.position = finalPos;

			hotPoint.transform.localScale = Vector3.one * (box.HotBoxSize / 500.0f);
			hotPoint.AlwaysOnTop = Camera.main.orthographic;
		}
		else if (hotPoint != null)
		{
			Destroy(hotPoint.gameObject);
			box.BoxHotPoint = null;
		}
	}

	void UpdateWorldPosBox(Box box, Vector3 roomPosition, bool isPlayer)
	{
		//worldpost unsync
		Box worldPos = box.BoxWorldPos;

		if (isPlayer && ((box.WorldPosition.x + box.Mod.x) != box.BoundingPos.x || (box.WorldPosition.z + box.Mod.z) != box.BoundingPos.z))
		{
			if (worldPos == null)
			{
				worldPos = Instantiate(BoxPrefab);
				worldPos.name = "WorldPos";
				worldPos.Color = new Color32(255, 0, 0, 128);
				Destroy(worldPos.gameObject.GetComponent<BoxCollider>());
				box.BoxWorldPos = worldPos;
			}

			Vector3 finalPos = (box.WorldPosition + box.Mod) / 1000.0f;
			float height = -box.BoundingPos.y / 1000.0f;
			finalPos = new Vector3(finalPos.x, height + 0.001f, finalPos.z) + roomPosition;
			worldPos.transform.position = finalPos;
			worldPos.transform.localScale = box.transform.localScale;
			worldPos.AlwaysOnTop = Camera.main.orthographic;
		}
		else if (worldPos != null)
		{
			Destroy(worldPos.gameObject);
			box.BoxWorldPos = null;
		}
	}

	public void UpdateBoxInfo()
	{
		BoxInfo.Clear();
		if (Player != null)
		{
			float angle = Player.Angles.y * 360.0f / 1024.0f;
			float sideAngle = (angle + 45.0f) % 90.0f - 45.0f;

			BoxInfo.Append("Position", Player.LocalPosition + Player.Mod);
			BoxInfo.Append("Angle", "{0:N1} {1:N1}", angle, sideAngle);
		}

		if (ShowAITD1Vars || ShowAdditionalInfo)
		{
			if(Player != null) BoxInfo.AppendLine();

			if (ShowAITD1Vars)
			{
				int calculatedFps = previousFramesCount.Sum();
				TimeSpan totalDelayTS = TimeSpan.FromSeconds(totalDelay.Elapsed);

				BoxInfo.Append("Timer 1", "{0}.{1:D2}", TimeSpan.FromSeconds(InternalTimer1 / 60), InternalTimer1 % 60);
				BoxInfo.Append("Timer 2", "{0}.{1:D2}", TimeSpan.FromSeconds(internalTimer2 / 60), internalTimer2 % 60);
				BoxInfo.Append("FPS/Delay", "{0}; {1} ms", calculatedFps, Mathf.FloorToInt(lastDelay * 1000));
				BoxInfo.Append("Total delay", "{0:D2}:{1:D2}:{2:D2}.{3:D3} ", totalDelayTS.Hours, totalDelayTS.Minutes, totalDelayTS.Seconds, totalDelayTS.Milliseconds);
			}

			Vector3 mousePosition = GetMousePosition(linkroom, linkfloor);
			BoxInfo.Append("Cursor position", "{0} {1}", Mathf.Clamp((int)(mousePosition.x), -32768, 32767), Mathf.Clamp((int)(mousePosition.z), -32768, 32767));
			if(Player != null) BoxInfo.Append("Last offset/dist", "{0}; {1}", Player.LastOffset, Mathf.RoundToInt(Player.LastDistance));

			if (ShowAITD1Vars)
			{
				BoxInfo.Append("Allow inventory", allowInventory ? "Yes" : "No");
				BoxInfo.Append("In hand", inHand);
			}
		}

		BoxInfo.UpdateText();
	}

	public void Update()
	{
		if (Input.GetKeyDown(KeyCode.Q))
		{
			totalDelay.Reset();
		}
		if (Input.GetKeyDown(KeyCode.W))
		{
			foreach (Box box in Actors.GetComponentsInChildren<Box>(true))
			{
				box.LastDistance = 0.0f;
			}
		}
		if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && IsCDROMVersion && ProcessReader != null)
		{
			if (Input.GetKeyDown(KeyCode.Alpha1))
			{
				//internal timer 1
				InternalTimer1 -= 60 * 5; //back 5 frames
				memory.Write(InternalTimer1, 0);
				ProcessReader.Write(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6, 4);
			}
			if (Input.GetKeyDown(KeyCode.Alpha2))
			{
				//internal timer 2
				internalTimer2 -= 60 * 5; //back 5 frames
				memory.Write(internalTimer2, 0);
				ProcessReader.Write(memory, Shared.ActorsMemoryAdress - 0x83B6 - 6 + 0xA5CE, 2);
			}
		}

	}

	public void CalculateFPS()
	{
		if (ProcessReader != null && ShowAITD1Vars)
		{
			//fps
			ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6, 2);
			int fps = memory.ReadShort(0);

			//frames counter (reset to zero when every second by AITD)
			ProcessReader.Read(memory, Shared.ActorsMemoryAdress - 0x83B6 + 0x7464, 2);
			int frames = memory.ReadShort(0);

			//check how much frames elapsed since last time
			int diff;
			if (frames >= oldFramesCount)
				diff = frames - oldFramesCount; //eg: 15 - 20
			else
				diff = fps - oldFramesCount + frames; //special case: eg: 60 - 58 + 3
			oldFramesCount = frames;

			if(delayCounter.Elapsed >= 0.1f) //100ms
			{
				lastDelay = delayCounter.Elapsed;
			}

			//check for large delays
			if (!saveTimerFlag)
			{
				delayCounter.Reset();
				totalDelay.Stop();
			}
			else
			{
				delayCounter.Start();
				totalDelay.Start();
			}

			float time = Time.time;
			if (diff > 0)
			{
				previousFramesCount.Enqueue(diff);
				previousFrameTime.Enqueue(time);
			}

			//remove any frame info older than one second
			while (previousFrameTime.Count > 0 &&
				previousFrameTime.Peek() < (time - 1.0f))
			{
				previousFramesCount.Dequeue();
				previousFrameTime.Dequeue();
			}
		}
	}

	void FixBoundingWrap(ref float a, ref float b)
	{
		if(a > b)
		{
			if(a < -b)
			{
				b += 65536.0f;
			}
			else
			{
				a -= 65536.0f;
			}
		}
	}

	int[] GetAllDOSBOXProcesses()
	{
		int[] processIds = Process.GetProcesses()
			.Where(x => GetProcessName(x).StartsWith("DOSBOX", StringComparison.InvariantCultureIgnoreCase))
			.Select(x => x.Id)
			.ToArray();

		return processIds;
	}

	string GetProcessName(Process process)
	{
		try
		{
			//could fail because not enough permissions (eg : admin process)
			return process.ProcessName;
		}
		catch
		{
			return string.Empty;
		}
	}

	bool SearchForBytePattern(int patternIndex, out int processId, out long address)
	{
		int[] processIds = GetAllDOSBOXProcesses();
		if (!processIds.Any())
		{
			RightText.text = "Cannot find DOSBOX process";
			processId = -1;
			address = -1;
			return false;
		}

		foreach (int pid in processIds)
		{
			ProcessMemoryReader reader = new ProcessMemoryReader(pid);
			var pattern = PlayerInitialPosition[patternIndex];
			long foundAddress = reader.SearchForBytePattern(pattern, true);
			if (foundAddress != -1)
			{
				processId = pid;
				address = foundAddress - 28 - playerInitialSlot[patternIndex] * actorStructSize[patternIndex];

				reader.Close();
				return true;
			}

			reader.Close();
		}

		processId = -1;
		address = -1;
		RightText.text = "Cannot find player data in DOSBOX process memory.";
		return false;
	}

	#region Room loader

	public bool LinkToDosBOX(int floor, int room, int detectedGame)
	{
		//search player position in DOSBOX processes
		int patternIndex = detectedGame - 1;

		int processId = Shared.ProcessId;
		if (processId == -1)
		{
			long memoryAddress;
			if (!SearchForBytePattern(patternIndex, out processId, out memoryAddress))
			{
				return false;
			}

			Shared.ProcessId = processId;
			Shared.ActorsMemoryAdress = memoryAddress;
			ProcessReader = new ProcessMemoryReader(processId);

			//vars
			if (patternIndex == 0) //AITD1 only
			{
				Shared.ObjectMemoryAddress = ProcessReader.SearchForBytePattern(objectMemoryPattern);

				if (Shared.ObjectMemoryAddress != -1)
				{
					Shared.ObjectMemoryAddress -= 4 + 52;

					//adjust actor offset (needed if player has been swapped)
					ProcessReader.Read(memory, Shared.ObjectMemoryAddress + 52, 2);
					int playerSlotID = memory.ReadShort(0);
					Shared.ActorsMemoryAdress += (1 - playerSlotID) * 160;
				}
			}
		}
		else
		{
			ProcessReader = new ProcessMemoryReader(processId);
		}

		//force reload
		linkfloor = floor;
		linkroom = room;

		dosBoxPattern = patternIndex;

		//check if CDROM/floppy version (AITD1 only)
		byte[] cdPattern = ASCIIEncoding.ASCII.GetBytes("CD Not Found");
		IsCDROMVersion = detectedGame == 1 && ProcessReader.SearchForBytePattern(cdPattern) != -1;

		RightText.text = string.Empty;
		return true;
	}

	public void UnlinkDosBox()
	{
		if (ProcessReader != null)
		{
			ProcessReader.Close();
			ProcessReader = null;
		}

		Shared.ProcessId = -1;
		BoxInfo.Clear(true);
		RightText.text = string.Empty;
		lastValidPlayerIndex = -1;
	}

	public void ResetCamera(int floor, int room)
	{
		lastPlayerPosition = Vector3.zero;
		linkfloor = floor;
		linkroom = room;
	}

	public long GetActorMemoryAddress(int index)
	{
		return Shared.ActorsMemoryAdress + index * actorStructSize[dosBoxPattern];
	}

	public Vector3 GetMousePosition(int room, int floor)
	{
		Vector3 cameraHeight = new Vector3(0.0f, 0.0f, Camera.main.transform.position.y);
		Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition + cameraHeight);
		Transform roomObject = GetComponent<RoomLoader>().GetRoom(floor, room);
		if (roomObject != null)
		{
			mousePosition -= roomObject.position;
		}
		return mousePosition * 1000.0f;
	}

	#endregion

	#region Exchange slots

	public void UpdateTargetSlot(Box highLightedBox)
	{
		if (highLightedBox != null && !GetComponent<WarpDialog>().warpMenuEnabled)
		{
			if (InputDigit(ref targetSlot))
			{
				UpdateTargetSlotText();
			}

			if (Input.GetKeyDown(KeyCode.Backspace))
			{
				targetSlot = targetSlot >= 10 ? targetSlot / 10 : -1;
				UpdateTargetSlotText();
			}

			if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
			{
				if (targetSlot >= 0 && targetSlot < 50)
				{
					ExchangeActorSlots(highLightedBox.Slot, targetSlot);
				}

				targetSlot = -1;
				UpdateTargetSlotText();
			}
		}
		else if (targetSlot != -1)
		{
			targetSlot = -1;
			UpdateTargetSlotText();
		}
	}

	void UpdateTargetSlotText()
	{
		RightText.text = (targetSlot == -1) ? string.Empty : string.Format("SLOT {0}", targetSlot);
	}

	bool InputDigit(ref int value)
	{
		int digit;
		if (IsKeypadKeyDown(out digit))
		{
			if (value == -1)
			{
				value = digit;
			}
			else
			{
				int newValue = digit + value * 10;
				if (newValue < 50)
				{
					value = newValue;
				}
			}

			return true;
		}

		return false;
	}

	bool IsKeypadKeyDown(out int value)
	{
		for(int digit = 0 ; digit <= 9 ; digit++)
		{
			if (Input.GetKeyDown(KeyCode.Keypad0 + digit)
			 || Input.GetKeyDown(KeyCode.Alpha0 + digit))
			{
				value = digit;
				return true;
			}
		}

		value = -1;
		return false;
	}

	void ExchangeActorSlots(int slotFrom, int slotTo)
	{
		if (ProcessReader != null && Shared.ObjectMemoryAddress != -1)
		{
			if (slotFrom != slotTo)
			{
				int actorSize = actorStructSize[dosBoxPattern];
				long offsetFrom = GetActorMemoryAddress(slotFrom);
				long offsetTo = GetActorMemoryAddress(slotTo);

				byte[] memoryFrom = new byte[actorSize];
				byte[] memoryTo = new byte[actorSize];

				//exchange slots
				ProcessReader.Read(memoryFrom, offsetFrom, actorSize);
				ProcessReader.Read(memoryTo, offsetTo, actorSize);

				ProcessReader.Write(memoryTo, offsetFrom, actorSize);
				ProcessReader.Write(memoryFrom, offsetTo, actorSize);

				UpdateObjectOwnerID(slotFrom, (short)slotTo);
				UpdateObjectOwnerID(slotTo, (short)slotFrom);
			}
		}
		else
		{
			RightText.text = "Actor swap is not available";
		}
	}

	void UpdateObjectOwnerID(int slotIndex, short ownerID)
	{
		int objectID = Boxes[slotIndex].ID;
		if (objectID != -1)
		{
			long address = Shared.ObjectMemoryAddress + objectID * 52;
			memory.Write(ownerID, 0);
			ProcessReader.Write(memory, address, 2);
		}
	}

	#endregion
}

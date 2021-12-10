﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocketIOSharp.Common;
using SocketIOSharp.Server;
using SocketIOSharp.Server.Client;

Console.WriteLine("Starting RoboScapeSimulator...");

/// <summary>
/// Frequency to send update messages to users
/// </summary>
const int updateFPS = 10;

/// <summary>
/// Frequency to run simulation at
/// </summary>
const int simFPS = 60;

JsonSerializer serializer = new();
serializer.NullValueHandling = NullValueHandling.Ignore;

/// <summary>
/// Mapping of room IDs to Room objects
/// </summary>
ConcurrentDictionary<string, Room> rooms = new();

/// <summary>
/// Send the available rooms and environments to a socket
/// </summary>
void sendAvailableRooms(SocketIOSocket socket)
{
    Utils.sendAsJSON(socket, "availableRooms", new Dictionary<string, object> { { "availableRooms", rooms.Keys }, { "canCreate", rooms.Count(r => !r.Value.Hibernating) < SettingsManager.MaxRooms } });
    Utils.sendAsJSON(socket, "availableEnvironments", Room.ListEnvironments());
}


/// <summary>
/// Send the rooms created  and environments to a socket
/// </summary>
void sendUserRooms(SocketIOSocket socket, string user)
{
    var userRooms = rooms.Where(pair => pair.Value.Creator == user).Select(pair => pair.Key).ToList();
    Utils.sendAsJSON(socket, "availableRooms", new Dictionary<string, object> { { "availableRooms", userRooms }, { "canCreate", rooms.Count(r => !r.Value.Hibernating) < SettingsManager.MaxRooms } });
    Utils.sendAsJSON(socket, "availableEnvironments", Room.ListEnvironments());
}

using (SocketIOServer server = new(new SocketIOServerOption(9001)))
{
    // Socket.io setup
    server.OnConnection((SocketIOSocket socket) =>
    {
        string? socketRoom = null;

        Console.WriteLine("Client connected!");

        // Cleanup a bit on disconnect
        socket.On(SocketIOEvent.DISCONNECT, () =>
        {
            Console.WriteLine("Client disconnected!");
            if (socketRoom != null)
            {
                rooms[socketRoom].RemoveSocket(socket);
            }
        });

        // Send room info
        socket.On("getRooms", (JToken[] args) =>
        {
            var user = (string)args[0];
            Console.WriteLine("init " + user);
            sendUserRooms(socket, user);
            Console.WriteLine("init2 " + user);
        });

        socket.On("joinRoom", (JToken[] args) =>
        {
            // Remove from existing room
            if (socketRoom != null)
            {
                rooms[socketRoom].RemoveSocket(socket);
            }

            // Create room if requested
            string roomID = (string)args[0]["roomID"];
            if (roomID == "create")
            {
                // Verify we have capacity
                if (rooms.Count(r => !r.Value.Hibernating) >= SettingsManager.MaxRooms)
                {
                    socket.Emit("error", "Failed to create room: insufficient resources");
                    return;
                }

                Room newRoom = new("", (string)args[0]["password"] ?? "", (string)args[0]["env"] ?? "default");

                if ((string)args[0]["namespace"] != null)
                {
                    newRoom.Name += "@" + (string)args[0]["namespace"];

                    // For current NetsBlox implementation, namespace is username of creating user
                    newRoom.Creator = (string)args[0]["namespace"];
                }

                rooms[newRoom.Name] = newRoom;
                socketRoom = newRoom.Name;
            }
            else
            {
                // Joining existing room, make sure it exists first
                if (rooms.ContainsKey(roomID))
                {
                    if (rooms[roomID].Password == "" || rooms[roomID].Password == (string)args[0]["password"])
                    {
                        rooms[roomID].Hibernating = false;
                        socketRoom = (string)args[0]["roomID"];
                    }
                }
            }

            if (socketRoom != null)
            {
                // Setup updates for socket in new room 
                rooms[socketRoom].AddSocket(socket);
                Utils.sendAsJSON(socket, "roomJoined", socketRoom);
                Utils.sendAsJSON(socket, "roomInfo", rooms[socketRoom].GetInfo());
                Utils.sendAsJSON(socket, "fullUpdate", rooms[socketRoom].SimInstance.GetBodies());
            }
            else
            {
                // Join failed
                Utils.sendAsJSON(socket, "roomJoined", false);
                Console.WriteLine("Failed attempt to join room " + roomID);
            }
        });
    });

    server.Start();

    Stopwatch stopwatch = new();
    stopwatch.Start();

    Console.WriteLine("Server started");

    // Client update loops
    var clientUpdateTimer = new System.Timers.Timer(1000d / updateFPS);

    clientUpdateTimer.Elapsed += (source, e) =>
    {
        foreach (Room room in rooms.Values)
        {
            if (room.SkipNextUpdate)
            {
                room.SkipNextUpdate = false;
                continue;
            }

            using var writer = new JTokenWriter();
            serializer.Serialize(writer, room.SimInstance.GetBodies(true));

            foreach (var socket in room.activeSockets)
            {
                socket.Emit("update", writer.Token);
            }
        }

    };

    clientUpdateTimer.Start();


    var clientFullUpdateTimer = new System.Timers.Timer(60000d);

    clientFullUpdateTimer.Elapsed += (source, e) =>
    {
        foreach (Room room in rooms.Values)
        {
            using (var writer = new JTokenWriter())
            {
                serializer.Serialize(writer, room.SimInstance.GetBodies());
                foreach (var socket in room.activeSockets)
                {
                    socket.Emit("fullUpdate", writer.Token);
                    room.SkipNextUpdate = true;
                }
            }
        }
    };

    clientFullUpdateTimer.Start();

    var cleanDeadRoomsTimer = new System.Timers.Timer(600000d);

    cleanDeadRoomsTimer.Elapsed += (source, e) =>
    {
        // If room is Hibernating and past its TTL, remove it
        var oldRooms = rooms.Where(pair => pair.Value.Hibernating && (DateTime.Now - pair.Value.LastInteractionTime).TotalSeconds > pair.Value.MaxHibernateTime).ToList();

        if (oldRooms.Count > 0)
        {
            Console.WriteLine($"Removing {oldRooms.Count} old rooms");
            oldRooms.ForEach(pair => rooms.TryRemove(pair));
        }
    };

    cleanDeadRoomsTimer.Start();

    var fpsSpan = TimeSpan.FromSeconds(1d / simFPS);
    Thread.Sleep(Math.Max(0, (int)fpsSpan.Subtract(stopwatch.Elapsed).TotalMilliseconds));

    // Simulation update loop
    while (true)
    {
        foreach (Room room in rooms.Values)
        {
            room.Update((float)stopwatch.Elapsed.TotalSeconds);
        }
        stopwatch.Restart();
        Thread.Sleep(Math.Max(0, (int)fpsSpan.Subtract(stopwatch.Elapsed).TotalMilliseconds));
    }
}

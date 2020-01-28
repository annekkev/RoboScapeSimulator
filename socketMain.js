const _ = require('lodash');
const debug = require('debug')('roboscape-sim:socketMain');

const Room = require('./src/Room');

const settings = {
    updateRate: 30,
    maxRobots: 5,
    maxRooms: 5
};

const rooms = [];

/**
 * @param {SocketIO.Server} io
 */
function socketMain(io) {
    function sendFullUpdate(socket, room) {
        socket.emit(
            'fullUpdate',
            _.keyBy(room.getBodies(false), body => body.label)
        );
    }

    function sendUpdate(socket, room) {
        let updateBodies = room.getBodies(true);

        if (updateBodies.length > 0) {
            socket.emit(
                'update',
                _.keyBy(updateBodies, body => body.label)
            );
        }
    }

    function joinRoom(roomID, socket) {
        let room = rooms[rooms.map(room => room.roomID).indexOf(roomID)];
        socket.join(roomID);

        // Create robot if not too many
        if (room.robots.length < settings.maxRobots) {
            // Add new robot and tell everyone about it
            room.addRobot();
            sendFullUpdate(io.to(roomID), room);
        } else {
            // Begin sending updates
            sendFullUpdate(socket, room);
        }
        // Temporary feature to reset example environment
        socket.on('reset', confirm => {
            if (confirm) {
                room.close();
                rooms[roomID] = new Room({ roomID: roomID });
                room = rooms[roomID];
                sendFullUpdate(io.to(roomID), room);
            }
        });
    }

    let updateInterval = setInterval(() => {
        for (let room of rooms) {
            // Check for dead bots
            if (room.removeDeadRobots() !== false) {
                sendFullUpdate(io.to(room.roomID), room);
            } else {
                sendUpdate(io.to(room.roomID), room);
            }
        }
    }, 1000 / settings.updateRate);

    io.on('connect', socket => {
        debug(`Socket ${socket.id} connected`);

        socket.emit('availableRooms', { availableRooms: rooms.map(room => room.roomID), canCreate: rooms.length < settings.maxRooms });

        let inRoom = false;

        // Allow joining a room
        socket.on('joinRoom', (data, cb) => {
            if (!inRoom) {
                let roomID = data.roomID;

                // Check that room is valid
                if (rooms.map(room => room.roomID).indexOf(roomID) !== -1) {
                    joinRoom(roomID, socket);
                    inRoom = true;
                    cb(roomID);
                } else if (roomID === 'create' && rooms.length < settings.maxRooms) {
                    debug(`Socket ${socket.id} requested to create room`);
                    // Create a virtual environment
                    let tempRoom = new Room();
                    rooms.push(tempRoom);
                    roomID = tempRoom.roomID;

                    joinRoom(roomID, socket);
                    cb(roomID);
                    inRoom = true;
                } else {
                    debug(`Socket ${socket.id} attempted to join invalid room!`);
                    cb(false);
                }
            } else {
                debug(`Socket ${socket.id} attempted to join second room!`);
                cb(false);
            }
        });
    });
}

module.exports = socketMain;

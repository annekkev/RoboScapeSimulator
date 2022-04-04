using System.Diagnostics;
using System.Numerics;
using RoboScapeSimulator.Entities;
using RoboScapeSimulator.Entities.Robots;
using RoboScapeSimulator.IoTScape.Devices;

namespace RoboScapeSimulator.Environments
{
    class LIDARRoadEnvironment : EnvironmentConfiguration
    {
        private string _courseType;

        public LIDARRoadEnvironment(string courseType = Courses.Easy)
        {
            Name = $"LIDAR Road ({courseType})";
            ID = "lidarroad" + courseType;
            Description = $"Robot with LIDAR drives down a {courseType} road";
            _courseType = courseType;
        }

        public struct Courses
        {
            public const string Easy = "Easy";
            public const string Hard = "Hard";
        }

        public override object Clone()
        {
            return new LIDARRoadEnvironment(_courseType);
        }

        public override void Setup(Room room)
        {
            Trace.WriteLine($"Setting up {Name} environment");

            // Ground
            var ground = new Ground(room, visualInfo: new VisualInfo() { Color = "#222" });


            float endPosZ = 7;
            float endPosX = 0;
            switch (_courseType)
            {
                case Courses.Hard:
                    // left
                    AddPath(room, new()
                    {
                        new(-0.5f, 0, 0),
                        new(-0.5f, 0, 1.75f),
                        new(1.5f, 0, 2.25f),
                        new(1.5f, 0, 2.75f),
                        new(-2.5f, 0, 3.5f),
                        new(-2.5f, 0, 5.25f),
                        new(1.5f, 0, 6.25f),
                        new(1.5f, 0, 8f),
                    });

                    // right
                    AddPath(room, new()
                    {
                        new(0.5f, 0, 0),
                        new(0.5f, 0, 0.75f),
                        new(2.5f, 0, 1.5f),
                        new(2.5f, 0, 3.5f),
                        new(-1.5f, 0, 4.25f),
                        new(-1.5f, 0, 4.5f),
                        new(2.5f, 0, 5.5f),
                        new(2.5f, 0, 8f),
                    });

                    // start area
                    AddPath(room, new()
                    {
                        new(-0.5f, 0, 0),
                        new(-0.5f, 0, -0.5f),
                        new(0.5f, 0, -0.5f),
                        new(0.5f, 0, 0f),
                    });

                    endPosZ = 8;
                    endPosX = 2;

                    break;
                case Courses.Easy:
                default:

                    // left
                    AddPath(room, new()
                    {
                        new(-0.5f, 0, 0),
                        new(-0.5f, 0, 1),
                        new(-1f, 0, 2.25f),
                        new(-1f, 0, 2.75f),
                        new(0.25f, 0, 4.75f),
                        new(0.25f, 0, 5.25f),
                        new(-0.5f, 0, 7f),
                    });

                    // right
                    AddPath(room, new()
                    {
                        new(0.5f, 0, 0),
                        new(0.5f, 0, 1),
                        new(0f, 0, 2.25f),
                        new(0f, 0, 2.75f),
                        new(1.25f, 0, 4.75f),
                        new(1.25f, 0, 5.25f),
                        new(0.5f, 0, 7f),
                    });

                    // start area
                    AddPath(room, new()
                    {
                        new(-0.5f, 0, 0),
                        new(-0.5f, 0, -0.5f),
                        new(0.5f, 0, -0.5f),
                        new(0.5f, 0, 0f),
                    });
                    break;
            }

            // Start and end areas
            var start = new Cube(room, 1, 0.01f, 1.1f, new(0, 0.005f, 0f), Quaternion.CreateFromYawPitchRoll(0, 0.005f, 0), isKinematic: true, visualInfo: new VisualInfo() { Color = "#D22" });
            var end = new Cube(room, 1, 0.01f, 1.1f, new(endPosX, 0.005f, endPosZ), Quaternion.CreateFromYawPitchRoll(0, -0.005f, 0), isKinematic: true, visualInfo: new VisualInfo() { Color = "#2D2" });

            // Demo robot
            var robot = new ParallaxRobot(room, new(0, 0.25f, 0), Quaternion.Identity);
            var lidar = new LIDARSensor(robot) { Offset = new(0, 0.1f, 0.07f), NumRays = 3, MinAngle = -2 * MathF.PI / 6f + MathF.PI / 2, MaxAngle = 2f * MathF.PI / 6 + MathF.PI / 2, MaxDistance = 5 };
            lidar.Setup(room);

            static void AddPath(Room room, List<Vector3> points, float thickness = 0.1f, float height = 0.5f)
            {
                Vector3 previous = points[0];
                foreach (var point in points)
                {
                    AddWall(room, previous, point, thickness, height);
                    previous = point;
                }
            }

            static Cube? AddWall(Room room, Vector3 p1, Vector3 p2, float thickness = 0.1f, float height = 0.5f, float padding = 0.025f)
            {
                var length = (p2 - p1).Length();

                if (length < 0.0001)
                {
                    return null;
                }

                var center = p1 + ((p2 - p1) * 0.5f);
                var angle = MathF.Atan2(p2.Z - p1.Z, p2.X - p1.X);
                return new Cube(room, length + padding, height, thickness, center, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angle), true, visualInfo: new VisualInfo() { Color = "#633" });
            }

        }
    }
}
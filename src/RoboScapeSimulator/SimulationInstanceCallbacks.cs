using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using RoboScapeSimulator.Entities;

namespace RoboScapeSimulator;

struct BodyCollisionProperties
{
    public BodyCollisionProperties() { Friction = 0.5f; Filter = new SubgroupCollisionFilter(); }

    public SubgroupCollisionFilter Filter;
    public float Friction;
    public bool NoCollide = false;
    public bool IsTrigger = false;
}

public struct SubgroupCollisionFilter
{
    /// <summary>
    /// A mask of 16 bits, each set bit representing a collision group that an object belongs to.
    /// </summary>
    public ushort SubgroupMembership;
    /// <summary>
    /// A mask of 16 bits, each set bit representing a collision group that an object can interact with.
    /// </summary>
    public ushort CollidableSubgroups;
    /// <summary>
    /// Id of the owner of the object. Objects belonging to different groups always collide.
    /// </summary>
    public int GroupId;

    /// <summary>
    /// Initializes a collision filter that collides with everything in the group.
    /// </summary>
    /// <param name="groupId">Id of the group that this filter operates within.</param>
    public SubgroupCollisionFilter(int groupId)
    {
        GroupId = groupId;
        SubgroupMembership = ushort.MaxValue;
        CollidableSubgroups = ushort.MaxValue;
    }

    /// <summary>
    /// Initializes a collision filter that belongs to one specific subgroup and can collide with any other subgroup.
    /// </summary>
    /// <param name="groupId">Id of the group that this filter operates within.</param>
    /// <param name="subgroupId">Id of the subgroup to put this collidable into.</param>
    public SubgroupCollisionFilter(int groupId, int subgroupId)
    {
        GroupId = groupId;
        Debug.Assert(subgroupId >= 0 && subgroupId < 16, "The subgroup field is a ushort; it can only hold 16 distinct subgroups.");
        SubgroupMembership = (ushort)(1 << subgroupId);
        CollidableSubgroups = ushort.MaxValue;
    }

    /// <summary>
    /// Disables a collision between this filter and the specified subgroup.
    /// </summary>
    /// <param name="subgroupId">Subgroup id to disable collision with.</param>
    public void DisableCollision(int subgroupId)
    {
        Debug.Assert(subgroupId >= 0 && subgroupId < 16, "The subgroup field is a ushort; it can only hold 16 distinct subgroups.");
        CollidableSubgroups ^= (ushort)(1 << subgroupId);
    }

    /// <summary>
    /// Modifies the interactable subgroups such that filterB does not interact with the subgroups defined by filter a and vice versa.
    /// </summary>
    /// <param name="a">Filter from which to remove collisions with filter b's subgroups.</param>
    /// <param name="b">Filter from which to remove collisions with filter a's subgroups.</param>
    public static void DisableCollision(ref SubgroupCollisionFilter filterA, ref SubgroupCollisionFilter filterB)
    {
        filterA.CollidableSubgroups &= (ushort)~filterB.SubgroupMembership;
        filterB.CollidableSubgroups &= (ushort)~filterA.SubgroupMembership;
    }

    /// <summary>
    /// Checks if the filters can collide by checking if b's membership can be collided by a's collidable groups.
    /// </summary>
    /// <param name="a">First filter to test.</param>
    /// <param name="b">Second filter to test.</param>
    /// <returns>True if the filters can collide, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AllowCollision(in SubgroupCollisionFilter a, in SubgroupCollisionFilter b)
    {
        return a.GroupId != b.GroupId || (a.CollidableSubgroups & b.SubgroupMembership) > 0;
    }

}

struct SimulationInstanceCallbacks : INarrowPhaseCallbacks
{
    public CollidableProperty<BodyCollisionProperties> Properties;
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity;
    public float FrictionCoefficient;

    public SimulationInstance SimInstance;

    public SimulationInstanceCallbacks(SimulationInstance simInstance, CollidableProperty<BodyCollisionProperties> properties)
    {
        SimInstance = simInstance;
        Properties = properties;
        ContactSpringiness = new(30, 1);
        MaximumRecoveryVelocity = 2f;
        FrictionCoefficient = 2f;
    }

    public void Initialize(Simulation simulation)
    {
        Properties.Initialize(simulation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
    {
        //It's impossible for two statics to collide, and pairs are sorted such that bodies always come before statics.
        if (b.Mobility != CollidableMobility.Static)
        {
            return SubgroupCollisionFilter.AllowCollision(Properties[a.BodyHandle].Filter, Properties[b.BodyHandle].Filter);
        }

        //While the engine won't even try creating pairs between statics at all, it will ask about kinematic-kinematic pairs.
        //Those pairs cannot emit constraints since both involved bodies have infinite inertia. Since most of the demos don't need
        //to collect information about kinematic-kinematic pairs, we'll require that at least one of the bodies needs to be dynamic.
        return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
    {
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        if (pair.B.Mobility != CollidableMobility.Static && (Properties[pair.A.BodyHandle].NoCollide || Properties[pair.B.BodyHandle].NoCollide || Properties[pair.A.BodyHandle].IsTrigger || Properties[pair.B.BodyHandle].IsTrigger))
        {
            pairMaterial.FrictionCoefficient = 0;
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30, 1);

            // Handle triggers
            if (Properties[pair.A.BodyHandle].IsTrigger || Properties[pair.B.BodyHandle].IsTrigger)
            {
                var triggerHandle = Properties[pair.A.BodyHandle].IsTrigger ? pair.A.BodyHandle : pair.B.BodyHandle;
                var otherHandle = Properties[pair.A.BodyHandle].IsTrigger ? pair.B.BodyHandle : pair.A.BodyHandle;

                // If environments begin to get very complex, this search may need to be replaced with a Dictionary lookup keyed on handle values
                var triggerEntity = SimInstance.Entities.Find(e =>
                    {
                        if (e is DynamicEntity d)
                        {
                            return d.BodyReference.Handle.Value == triggerHandle.Value;
                        }
                        return false;
                    }
                );

                if (triggerEntity != null)
                {
                    if (triggerEntity is Trigger trigger)
                    {
                        var other = SimInstance.Entities.Find(e =>
                            {
                                if (e is DynamicEntity d)
                                {
                                    return d.BodyReference.Handle.Value == otherHandle.Value;
                                }
                                return false;
                            }
                        );
                        trigger.EntityInside(other!);
                    }
                }
            }

            return false;
        }

        pairMaterial.FrictionCoefficient = Properties[pair.A.BodyHandle].Friction;
        if (pair.B.Mobility != CollidableMobility.Static)
        {
            //If two bodies collide, just average the friction.
            pairMaterial.FrictionCoefficient = (pairMaterial.FrictionCoefficient + Properties[pair.B.BodyHandle].Friction) * 0.5f;
        }
        pairMaterial.MaximumRecoveryVelocity = 2f;
        pairMaterial.SpringSettings = new SpringSettings(30, 1);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
    {
        return true;
    }

    public void Dispose()
    {
        Properties.Dispose();
    }
}


public struct SimulationInstanceIntegratorCallbacks : IPoseIntegratorCallbacks
{
    /// <summary>
    /// Gravity to apply to dynamic bodies in the simulation.
    /// </summary>
    public Vector3 Gravity;
    /// <summary>
    /// Fraction of dynamic body linear velocity to remove per unit of time. Values range from 0 to 1. 0 is fully undamped, while values very close to 1 will remove most velocity.
    /// </summary>
    public float LinearDamping;
    /// <summary>
    /// Fraction of dynamic body angular velocity to remove per unit of time. Values range from 0 to 1. 0 is fully undamped, while values very close to 1 will remove most velocity.
    /// </summary>
    public float AngularDamping;

    Vector3Wide gravityWideDt;
    Vector<float> linearDampingDt;
    Vector<float> angularDampingDt;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.ConserveMomentumWithGyroscopicTorque;

    /// <summary>
    /// Gets whether the integrator should use substepping for unconstrained bodies when using a substepping solver.
    /// If true, unconstrained bodies will be integrated with the same number of substeps as the constrained bodies in the solver.
    /// If false, unconstrained bodies use a single step of length equal to the dt provided to Simulation.Timestep. 
    /// </summary>
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;

    /// <summary>
    /// Gets whether the velocity integration callback should be called for kinematic bodies.
    /// If true, IntegrateVelocity will be called for bundles including kinematic bodies.
    /// If false, kinematic bodies will just continue using whatever velocity they have set.
    /// Most use cases should set this to false.
    /// </summary>
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation)
    {
        //In this demo, we don't need to initialize anything.
        //If you had a simulation with per body gravity stored in a CollidableProperty<T> or something similar, having the simulation provided in a callback can be helpful.
    }

    /// <summary>
    /// Creates a new set of simple callbacks for the demos.
    /// </summary>
    /// <param name="gravity">Gravity to apply to dynamic bodies in the simulation.</param>
    /// <param name="linearDamping">Fraction of dynamic body linear velocity to remove per unit of time. Values range from 0 to 1. 0 is fully undamped, while values very close to 1 will remove most velocity.</param>
    /// <param name="angularDamping">Fraction of dynamic body angular velocity to remove per unit of time. Values range from 0 to 1. 0 is fully undamped, while values very close to 1 will remove most velocity.</param>
    public SimulationInstanceIntegratorCallbacks(Vector3 gravity, float linearDamping = .03f, float angularDamping = .03f) : this()
    {
        Gravity = gravity;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
    }

    public void PrepareForIntegration(float dt)
    {
        //No reason to recalculate gravity * dt for every body; just cache it ahead of time.
        //Since these callbacks don't use per-body damping values, we can precalculate everything.
        linearDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
        angularDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
        gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    /// <summary>
    /// Callback for a bundle of bodies being integrated.
    /// </summary>
    /// <param name="bodyIndices">Indices of the bodies being integrated in this bundle.</param>
    /// <param name="position">Current body positions.</param>
    /// <param name="orientation">Current body orientations.</param>
    /// <param name="localInertia">Body's current local inertia.</param>
    /// <param name="integrationMask">Mask indicating which lanes are active in the bundle. Active lanes will contain 0xFFFFFFFF, inactive lanes will contain 0.</param>
    /// <param name="workerIndex">Index of the worker thread processing this bundle.</param>
    /// <param name="dt">Durations to integrate the velocity over. Can vary over lanes.</param>
    /// <param name="velocity">Velocity of bodies in the bundle. Any changes to lanes which are not active by the integrationMask will be discarded.</param>
    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
    {
        //This is a handy spot to implement things like position dependent gravity or per-body damping.
        //This implementation uses a single damping value for all bodies that allows it to be precomputed.
        //We don't have to check for kinematics; IntegrateVelocityForKinematics returns false, so we'll never see them in this callback.
        //Note that these are SIMD operations and "Wide" types. There are Vector<float>.Count lanes of execution being evaluated simultaneously.
        //The types are laid out in array-of-structures-of-arrays (AOSOA) format. That's because this function is frequently called from vectorized contexts within the solver.
        //Transforming to "array of structures" (AOS) format for the callback and then back to AOSOA would involve a lot of overhead, so instead the callback works on the AOSOA representation directly.
        velocity.Linear = (velocity.Linear + gravityWideDt) * linearDampingDt;
        velocity.Angular *= angularDampingDt;
    }
}
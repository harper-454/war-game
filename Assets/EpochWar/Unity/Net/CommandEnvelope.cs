using System;
using Unity.Collections;
using Unity.Netcode;
using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Unity.Net
{
    /// <summary>
    /// Discriminates which concrete <see cref="ICommand"/> a <see cref="CommandEnvelope"/> carries.
    /// The byte tag is the first thing written on the wire so both writer and reader can branch to
    /// serialize only the fields that command actually needs.
    /// </summary>
    public enum NetCommandKind : byte
    {
        None = 0,
        StartResearch = 1,
        AdvanceEra = 2,
        RecruitUnit = 3,
        PlaceStructure = 4,
        Move = 5,
        FormBattalion = 6,
        DisbandBattalion = 7,
        DeployDoomsday = 8,
        LaunchColonyShip = 9,
    }

    /// <summary>
    /// The serializable wire form of an <see cref="ICommand"/> intent sent from a client to the Host
    /// (Req 8.2, 8.5).
    ///
    /// <para><b>Why an envelope?</b> The engine-free <c>EpochWar.Core</c> commands are polymorphic
    /// C# objects; Netcode for GameObjects cannot send a base-class reference over the wire. Rather
    /// than make every Core command depend on Netcode (which would break the "no UnityEngine / no
    /// networking in Core" boundary), this single flattened, <see cref="INetworkSerializable"/> value
    /// type carries a <see cref="NetCommandKind"/> tag plus a superset of the fields any one command
    /// needs. The Host reconstructs the exact concrete command from the tag with
    /// <see cref="ToCommand"/> and feeds it through the <em>same</em> authoritative
    /// <see cref="CommandRouter"/> path an AI command takes, so a human intent and an equivalent AI
    /// command resolve identically (Req 8.5, Property 31).</para>
    ///
    /// <para><b>Serialization approach.</b> <see cref="NetworkSerialize{T}"/> always writes the
    /// <see cref="Kind"/> tag and the <see cref="IssuingNationId"/> first; it then writes only the
    /// fields relevant to that <see cref="Kind"/>, in a fixed order, so the payload stays compact
    /// (e.g. an <c>AdvanceEra</c> command serializes to just the tag + nation id, while a
    /// <c>Move</c> carries the destination and either a battalion id or an explicit unit-id list).
    /// Because the reader reads the tag first, it takes the identical branch and reads back exactly
    /// the same fields the writer wrote. Variable-length data (the unit-id array, the string id) is
    /// length-prefixed and hand-serialized so there is no dependency on optional Netcode array
    /// helpers. String ids (tech/unit/structure ids and battalion names) ride in a
    /// <see cref="FixedString64Bytes"/>, which is ample for the catalog's short identifiers and
    /// avoids a heap allocation on the wire.</para>
    ///
    /// <para>The envelope carries no gameplay logic and never validates: acceptance/rejection is
    /// decided by the owning system's handler when the reconstructed command is dispatched on the
    /// Host, and reported as a <see cref="CommandResult"/> (never thrown).</para>
    /// </summary>
    public struct CommandEnvelope : INetworkSerializable
    {
        // ---- Always present ----
        public NetCommandKind Kind;
        public int IssuingNationId;

        // ---- Shared scalar slots (meaning depends on Kind) ----
        // PrimaryId reuse:
        //   RecruitUnit      -> issuing StructureId
        //   DisbandBattalion -> BattalionId
        //   DeployDoomsday   -> target NationId
        //   LaunchColonyShip -> ColonyShip UnitId
        public int PrimaryId;

        // StrA reuse:
        //   StartResearch / DeployDoomsday -> TechnologyId
        //   RecruitUnit                    -> UnitId (UnitDef id)
        //   PlaceStructure                 -> StructureId (StructureDef id)
        //   FormBattalion                  -> Battalion display Name
        public FixedString64Bytes StrA;

        // Cell reuse:
        //   PlaceStructure -> footprint Origin
        //   Move           -> Destination
        public NetCoord Cell;

        // Move target selection.
        public bool HasBattalion;   // true => Move targets BattalionId; false => Move targets UnitIds
        public int BattalionId;

        // Explicit unit id list for Move (per-unit) and FormBattalion. Never null after decode.
        public int[] UnitIds;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            // The tag and issuing nation are unconditional; everything after is written per-kind so
            // the wire payload only ever contains the fields the specific command actually uses.
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref IssuingNationId);

            switch (Kind)
            {
                case NetCommandKind.StartResearch:
                    serializer.SerializeValue(ref StrA); // TechnologyId
                    break;

                case NetCommandKind.AdvanceEra:
                    // No extra fields — the tag + issuing nation fully describe the intent.
                    break;

                case NetCommandKind.RecruitUnit:
                    serializer.SerializeValue(ref PrimaryId); // StructureId
                    serializer.SerializeValue(ref StrA);      // UnitDef id
                    break;

                case NetCommandKind.PlaceStructure:
                    serializer.SerializeValue(ref StrA); // StructureDef id
                    serializer.SerializeValue(ref Cell); // footprint origin
                    break;

                case NetCommandKind.Move:
                    serializer.SerializeValue(ref Cell);         // destination
                    serializer.SerializeValue(ref HasBattalion);
                    if (HasBattalion)
                    {
                        serializer.SerializeValue(ref BattalionId);
                    }
                    else
                    {
                        SerializeIntArray(serializer, ref UnitIds);
                    }
                    break;

                case NetCommandKind.FormBattalion:
                    serializer.SerializeValue(ref StrA); // Battalion name
                    SerializeIntArray(serializer, ref UnitIds);
                    break;

                case NetCommandKind.DisbandBattalion:
                    serializer.SerializeValue(ref PrimaryId); // BattalionId
                    break;

                case NetCommandKind.DeployDoomsday:
                    serializer.SerializeValue(ref StrA);      // weapon TechnologyId
                    serializer.SerializeValue(ref PrimaryId); // target NationId
                    break;

                case NetCommandKind.LaunchColonyShip:
                    serializer.SerializeValue(ref PrimaryId); // ColonyShip UnitId
                    break;
            }
        }

        /// <summary>
        /// Length-prefixed hand-serialization of an <see cref="int"/> array so variable-length unit
        /// lists round-trip without relying on optional Netcode array overloads. On read the array is
        /// always (re)allocated to the transmitted length, so it is never null after decode.
        /// </summary>
        private static void SerializeIntArray<T>(BufferSerializer<T> serializer, ref int[] values)
            where T : IReaderWriter
        {
            int count = values?.Length ?? 0;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                values = new int[count < 0 ? 0 : count];
            }

            for (int i = 0; i < values.Length; i++)
            {
                serializer.SerializeValue(ref values[i]);
            }
        }

        /// <summary>
        /// Reconstructs the concrete engine-free <see cref="ICommand"/> this envelope describes so the
        /// Host can dispatch it through the shared authoritative router (Req 8.5). Returns
        /// <c>null</c> for an unrecognized <see cref="Kind"/>; the caller drops such messages rather
        /// than throwing.
        /// </summary>
        public ICommand ToCommand()
        {
            switch (Kind)
            {
                case NetCommandKind.StartResearch:
                    return new StartResearchCommand(IssuingNationId, StrA.ToString());

                case NetCommandKind.AdvanceEra:
                    return new AdvanceEraCommand(IssuingNationId);

                case NetCommandKind.RecruitUnit:
                    return new RecruitUnitCommand(IssuingNationId, PrimaryId, StrA.ToString());

                case NetCommandKind.PlaceStructure:
                    return new PlaceStructureCommand(IssuingNationId, StrA.ToString(), Cell.ToCellCoord());

                case NetCommandKind.Move:
                    return HasBattalion
                        ? new MoveCommand(IssuingNationId, BattalionId, Cell.ToCellCoord())
                        : new MoveCommand(IssuingNationId, UnitIds ?? Array.Empty<int>(), Cell.ToCellCoord());

                case NetCommandKind.FormBattalion:
                    return new FormBattalionCommand(IssuingNationId, StrA.ToString(), UnitIds ?? Array.Empty<int>());

                case NetCommandKind.DisbandBattalion:
                    return new DisbandBattalionCommand(IssuingNationId, PrimaryId);

                case NetCommandKind.DeployDoomsday:
                    return new DeployDoomsdayCommand(IssuingNationId, StrA.ToString(), PrimaryId);

                case NetCommandKind.LaunchColonyShip:
                    return new LaunchColonyShipCommand(IssuingNationId, PrimaryId);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Flattens a concrete engine-free <see cref="ICommand"/> into its wire envelope for a
        /// client-to-Host <c>ServerRpc</c>. Throws <see cref="NotSupportedException"/> for a command
        /// type that has no envelope mapping so a missing case surfaces immediately in development
        /// rather than being silently dropped.
        /// </summary>
        public static CommandEnvelope From(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var envelope = new CommandEnvelope
            {
                IssuingNationId = command.IssuingNationId,
                UnitIds = Array.Empty<int>(),
            };

            switch (command)
            {
                case StartResearchCommand c:
                    envelope.Kind = NetCommandKind.StartResearch;
                    envelope.StrA = Truncate(c.TechnologyId);
                    break;

                case AdvanceEraCommand _:
                    envelope.Kind = NetCommandKind.AdvanceEra;
                    break;

                case RecruitUnitCommand c:
                    envelope.Kind = NetCommandKind.RecruitUnit;
                    envelope.PrimaryId = c.StructureId;
                    envelope.StrA = Truncate(c.UnitId);
                    break;

                case PlaceStructureCommand c:
                    envelope.Kind = NetCommandKind.PlaceStructure;
                    envelope.StrA = Truncate(c.StructureId);
                    envelope.Cell = NetCoord.Of(c.Origin);
                    break;

                case MoveCommand c:
                    envelope.Kind = NetCommandKind.Move;
                    envelope.Cell = NetCoord.Of(c.Destination);
                    if (c.BattalionId.HasValue)
                    {
                        envelope.HasBattalion = true;
                        envelope.BattalionId = c.BattalionId.Value;
                    }
                    else
                    {
                        envelope.HasBattalion = false;
                        envelope.UnitIds = ToArray(c.UnitIds);
                    }
                    break;

                case FormBattalionCommand c:
                    envelope.Kind = NetCommandKind.FormBattalion;
                    envelope.StrA = Truncate(c.Name);
                    envelope.UnitIds = ToArray(c.UnitIds);
                    break;

                case DisbandBattalionCommand c:
                    envelope.Kind = NetCommandKind.DisbandBattalion;
                    envelope.PrimaryId = c.BattalionId;
                    break;

                case DeployDoomsdayCommand c:
                    envelope.Kind = NetCommandKind.DeployDoomsday;
                    envelope.StrA = Truncate(c.TechnologyId);
                    envelope.PrimaryId = c.TargetNationId;
                    break;

                case LaunchColonyShipCommand c:
                    envelope.Kind = NetCommandKind.LaunchColonyShip;
                    envelope.PrimaryId = c.ColonyShipUnitId;
                    break;

                default:
                    throw new NotSupportedException(
                        $"No CommandEnvelope mapping for command type {command.GetType().Name}.");
            }

            return envelope;
        }

        // Content ids/names are short catalog identifiers; a 64-byte fixed string holds them without
        // allocating. Guard against an over-long value so we never throw while packing the wire form.
        private static FixedString64Bytes Truncate(string value)
        {
            var s = new FixedString64Bytes();
            if (string.IsNullOrEmpty(value))
            {
                return s;
            }

            // FixedString64Bytes stores up to 61 UTF-8 bytes; Append copies what fits and reports the
            // rest as truncated, which is fine for the short ids the catalog uses.
            s.Append(value);
            return s;
        }

        private static int[] ToArray(System.Collections.Generic.IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return Array.Empty<int>();
            }

            var arr = new int[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                arr[i] = ids[i];
            }

            return arr;
        }
    }
}

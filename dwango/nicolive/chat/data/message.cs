// <auto-generated>
//   This file was generated by a tool; you should avoid making direct changes.
//   Consider using 'partial classes' to extend these types
//   Input: message.proto
// </auto-generated>

#region Designer generated code
#pragma warning disable CS0612, CS0618, CS1591, CS3021, CS8981, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192
namespace dwango.nicolive.chat.data
{

    [global::ProtoBuf.ProtoContract()]
    public partial class NicoliveMessage : global::ProtoBuf.IExtensible
    {
        private global::ProtoBuf.IExtension __pbn__extensionData;
        global::ProtoBuf.IExtension global::ProtoBuf.IExtensible.GetExtensionObject(bool createIfMissing)
            => global::ProtoBuf.Extensible.GetExtensionObject(ref __pbn__extensionData, createIfMissing);

        [global::ProtoBuf.ProtoMember(1)]
        public Chat chat
        {
            get => __pbn__data.Is(1) ? ((Chat)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(1, value);
        }
        public bool ShouldSerializechat() => __pbn__data.Is(1);
        public void Resetchat() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 1);

        private global::ProtoBuf.DiscriminatedUnionObject __pbn__data;

        [global::ProtoBuf.ProtoMember(7)]
        public SimpleNotification simple_notification
        {
            get => __pbn__data.Is(7) ? ((SimpleNotification)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(7, value);
        }
        public bool ShouldSerializesimple_notification() => __pbn__data.Is(7);
        public void Resetsimple_notification() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 7);

        [global::ProtoBuf.ProtoMember(8)]
        public Gift gift
        {
            get => __pbn__data.Is(8) ? ((Gift)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(8, value);
        }
        public bool ShouldSerializegift() => __pbn__data.Is(8);
        public void Resetgift() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 8);

        [global::ProtoBuf.ProtoMember(9)]
        public Nicoad nicoad
        {
            get => __pbn__data.Is(9) ? ((Nicoad)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(9, value);
        }
        public bool ShouldSerializenicoad() => __pbn__data.Is(9);
        public void Resetnicoad() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 9);

        [global::ProtoBuf.ProtoMember(13)]
        public GameUpdate game_update
        {
            get => __pbn__data.Is(13) ? ((GameUpdate)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(13, value);
        }
        public bool ShouldSerializegame_update() => __pbn__data.Is(13);
        public void Resetgame_update() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 13);

        [global::ProtoBuf.ProtoMember(17)]
        public TagUpdated tag_updated
        {
            get => __pbn__data.Is(17) ? ((TagUpdated)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(17, value);
        }
        public bool ShouldSerializetag_updated() => __pbn__data.Is(17);
        public void Resettag_updated() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 17);

        [global::ProtoBuf.ProtoMember(18)]
        public global::dwango.nicolive.chat.data.atoms.ModeratorUpdated moderator_updated
        {
            get => __pbn__data.Is(18) ? ((global::dwango.nicolive.chat.data.atoms.ModeratorUpdated)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(18, value);
        }
        public bool ShouldSerializemoderator_updated() => __pbn__data.Is(18);
        public void Resetmoderator_updated() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 18);

        [global::ProtoBuf.ProtoMember(19)]
        public global::dwango.nicolive.chat.data.atoms.SSNGUpdated ssng_updated
        {
            get => __pbn__data.Is(19) ? ((global::dwango.nicolive.chat.data.atoms.SSNGUpdated)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(19, value);
        }
        public bool ShouldSerializessng_updated() => __pbn__data.Is(19);
        public void Resetssng_updated() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 19);

        [global::ProtoBuf.ProtoMember(20)]
        public Chat overflowed_chat
        {
            get => __pbn__data.Is(20) ? ((Chat)__pbn__data.Object) : default;
            set => __pbn__data = new global::ProtoBuf.DiscriminatedUnionObject(20, value);
        }
        public bool ShouldSerializeoverflowed_chat() => __pbn__data.Is(20);
        public void Resetoverflowed_chat() => global::ProtoBuf.DiscriminatedUnionObject.Reset(ref __pbn__data, 20);

    }

}

#pragma warning restore CS0612, CS0618, CS1591, CS3021, CS8981, IDE0079, IDE1006, RCS1036, RCS1057, RCS1085, RCS1192
#endregion
using System;
using System.Runtime.Serialization;

namespace Univer.Desktop.Web
{
    // Typed auxData is required: the pinned DevTools DTO exposes it as System.Object
    // and loses frameId during deserialization. All values come from Chromium itself.
    [DataContract]
    internal sealed class RuntimeContextEvent : EventArgs
    {
        [DataMember(Name = "context")] public RuntimeContext Context { get; set; }
    }
    [DataContract]
    internal sealed class RuntimeContext
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "origin")] public string Origin { get; set; }
        [DataMember(Name = "auxData")] public RuntimeFrameData AuxData { get; set; }
    }
    [DataContract]
    internal sealed class RuntimeFrameData
    {
        [DataMember(Name = "frameId")] public string FrameId { get; set; }
        [DataMember(Name = "isDefault")] public bool IsDefault { get; set; }
    }
}

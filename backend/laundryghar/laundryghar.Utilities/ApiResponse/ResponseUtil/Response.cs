using System.Runtime.Serialization;
using laundryghar.Utilities.ApiResponse.IResponseUtil;

namespace laundryghar.Utilities.ApiResponse.ResponseUtil;

[DataContract]
public class Response : IResponse
{
    [DataMember(EmitDefaultValue = false)]
    public Message? Message { get; set; }

    [DataMember]
    public bool Status { get; set; }
}

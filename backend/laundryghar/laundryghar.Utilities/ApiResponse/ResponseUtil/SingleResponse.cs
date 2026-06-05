using System.Runtime.Serialization;
using laundryghar.Utilities.ApiResponse.IResponseUtil;

namespace laundryghar.Utilities.ApiResponse.ResponseUtil;

[DataContract]
public class SingleResponse<TModel> : ISingleResponse<TModel>
{
    [DataMember(EmitDefaultValue = false)]
    public Message? Message { get; set; }

    [DataMember]
    public bool Status { get; set; }

    [DataMember(EmitDefaultValue = false)]
    public TModel? Data { get; set; }
}

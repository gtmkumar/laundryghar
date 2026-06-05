using System.Runtime.Serialization;
using laundryghar.Utilities.ApiResponse.IResponseUtil;
using laundryghar.Utilities.Common;

namespace laundryghar.Utilities.ApiResponse.ResponseUtil;

[DataContract]
public class PaginatedListResponse<TModel> : IPaginatedListResponse<TModel>
{
    [DataMember(EmitDefaultValue = false)]
    public Message? Message { get; set; }

    [DataMember]
    public bool Status { get; set; }

    [DataMember(EmitDefaultValue = false)]
    public PaginatedList<TModel>? Data { get; set; }
}

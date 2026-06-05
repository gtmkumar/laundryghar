using System.Runtime.Serialization;

namespace laundryghar.Utilities.Results;

[DataContract(Namespace = "")]
public enum ResultMessage
{
    [EnumMember(Value = "Success")]
    SuccessMessage,

    [EnumMember(Value = "Error")]
    ErrorMessage,

    [EnumMember(Value = "ExceptionDuringOperation")]
    ExceptionMessage
}

namespace laundryghar.Utilities.ApiResponse.IResponseUtil;

public interface ISingleResponse<TModel> : IResponse
{
    TModel? Data { get; set; }
}

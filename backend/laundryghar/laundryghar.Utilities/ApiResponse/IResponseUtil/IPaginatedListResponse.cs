using laundryghar.Utilities.Common;

namespace laundryghar.Utilities.ApiResponse.IResponseUtil;

public interface IPaginatedListResponse<TModel> : IResponse
{
    PaginatedList<TModel>? Data { get; set; }
}

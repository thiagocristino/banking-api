namespace BankingApi.Exceptions;

public class BusinessException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }

    public BusinessException(
        string code,
        string message,
        int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
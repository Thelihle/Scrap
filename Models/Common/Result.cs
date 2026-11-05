namespace SCP_Control.Models.Common
{
    public class Result
    {
        public bool Success { get; }
        public string Message { get; }

        protected Result(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static Result CreateSuccess(string message = "") =>
            new Result(true, message);

        public static Result CreateFailure(string message) =>
            new Result(false, message);
    }

    public class Result<T>
    {
        public bool Success { get; }
        public string Message { get; }
        public T? Data { get; }

        private Result(bool success, string message, T? data = default)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public static Result<T> CreateSuccess(string message = "Operation successful", T? data = default)
        {
            return new Result<T>(true, message, data);
        }

        public static Result<T> CreateFailure(string message)
        {
            return new Result<T>(false, message);
        }
    }

    public class OperationResult : Result
    {
        public object? Data { get; }

        private OperationResult(bool success, string message, object? data = null)
            : base(success, message)
        {
            Data = data;
        }

        public static OperationResult Succeeded(string message = "Operation successful", object? data = null)
        {
            return new OperationResult(true, message, data);
        }

        public static OperationResult Failed(string message)
        {
            return new OperationResult(false, message);
        }
    }
}
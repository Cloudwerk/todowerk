namespace TodoWerk.SharedKernel;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,

    /// <summary>
    /// Signed in, and still not allowed. The distinction from <see cref="Unauthorized"/> is the
    /// whole of what a denied Licence means: signing in again is exactly the wrong advice, and a
    /// 401 is what the client turns into a sign-in prompt.
    /// </summary>
    Forbidden = 5,
}

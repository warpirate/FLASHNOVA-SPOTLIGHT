namespace FlashSpot.Core.Abstractions;

public interface ICalculatorService
{
    bool TryEvaluate(string query, out string value);
}


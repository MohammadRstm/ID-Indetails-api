namespace ID_Indetails.api.Enums;

/// <summary>How what was left of a salary advance was closed, instead of coming off later pay.</summary>
public enum SalaryAdvanceSettlement
{
    /// <summary>The person paid the rest back in cash.</summary>
    Cash,
    /// <summary>The company gave up on the rest: a company expense on the day it was written off.</summary>
    WrittenOff,
}

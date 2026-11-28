namespace Coglatas.Domain.Enums;

public enum FormPurpose
{
    Ordinary = 0,
    Survey = 1
}

public enum SurveyFieldType
{
    Text = 0,
    Number = 1,
    Scale = 2,
    Date = 3,
    Boolean = 4,
    SingleChoice = 5,
    MultipleChoice = 6
}

public enum FormSubmissionStatus
{
    Submitted = 0,
    Voided = 1
}

public enum PresentationParticipantRole
{
    Presenter = 0,
    Reviewer = 1
}

public enum SurveyNameMode
{
    Anonymous = 0,
    OptionalName = 1,
    RequiredName = 2
}

public enum SurveyAccountBinding
{
    Off = 0,
    ExplicitOptIn = 1
}

[Flags]
public enum SurveyProgramFields
{
    None = 0,
    Title = 1,
    Time = 2,
    Venue = 4,
    PresenterNames = 8,
    TimeZone = 16
}

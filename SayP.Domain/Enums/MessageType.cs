namespace SayP.Domain.Enums;

public enum MessageType
{
    Text = 0,
    Image = 1,
    Document = 2,
    Audio = 3,
    Video = 4,
    Interactive = 5,  // Buttons, Lists
    Template = 6,     // WhatsApp templates
    Location = 7,
    Contact = 8
}

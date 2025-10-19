namespace SA_Project_API.Models
{
    public class User
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }

        // Stored as base64 strings
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
    }
}

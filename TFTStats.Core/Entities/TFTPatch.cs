namespace TFTStats.Core.Entities
{
    public record TFTPatch
    {
        public int Id { get;set;  }
        public int SetNumber { get;set;  }
        public string PatchName { get; set; } = null!;
        public DateTime StartDate{ get;set;  }
        public DateTime? EndDate{ get;set;  }
    }
}

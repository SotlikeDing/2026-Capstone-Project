using System;

namespace MobileApi.Models
{
    public class PhPricingResponse
    {
        public int Id { get; set; }
        public string GameType { get; set; } = "";
        public int HunterCount { get; set; }
        public decimal Price { get; set; }
        public string Status { get; set; } = "Aktief";
    }

    public class CreatePhPricingRequest
    {
        public int PhId { get; set; }
        public string GameType { get; set; } = "";
        public int HunterCount { get; set; }
        public decimal Price { get; set; }
    }

    public class VgPackageResponse
    {
        public int Id { get; set; }
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
        public string Status { get; set; } = "Aktief";
    }

    public class CreateVgPackageRequest
    {
        public int VgId { get; set; }
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
    }
}



using MobileApi.Models;

namespace MobileApi.Validation;

public static class TrophySaveRequestValidator
{
    public static string? GetError(TrophySaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Species))
        {
            return "Voer 'n spesie in.";
        }

        if (request.Species.Length > 120)
        {
            return "Die spesie mag nie langer as 120 karakters wees nie.";
        }

        if (request.PhotoPath.Length > 500)
        {
            return "Die fotoverwysing mag nie langer as 500 karakters wees nie.";
        }

        if (request.AntlerSizeCm is < 0)
        {
            return "Gewei- of horinggrootte kan nie negatief wees nie.";
        }

        if (request.CarcassWeightKg is < 0)
        {
            return "Karkasgewig kan nie negatief wees nie.";
        }

        if (request.ShotDistanceMeters is < 0)
        {
            return "Skootafstand kan nie negatief wees nie.";
        }

        if (request.FirearmType.Length > 120)
        {
            return "Die vuurwapentipe mag nie langer as 120 karakters wees nie.";
        }

        if (request.HarvestDate == default)
        {
            return "Kies 'n geldige oesdatum.";
        }

        if (request.LocationText.Length > 200)
        {
            return "Die ligging mag nie langer as 200 karakters wees nie.";
        }

        return null;
    }
}

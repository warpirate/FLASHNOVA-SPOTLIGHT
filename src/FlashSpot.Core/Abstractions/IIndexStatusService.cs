using FlashSpot.Core.Models;

namespace FlashSpot.Core.Abstractions;

public interface IIndexStatusService
{
    IndexStatusSnapshot GetStatus();
}


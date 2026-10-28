using System;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

internal interface IBaseEntity
{
    Guid Id { get; set; }
    DateTime CreatedDate { get; set; }
    DateTime LastModifiedDate { get; set; }
} 

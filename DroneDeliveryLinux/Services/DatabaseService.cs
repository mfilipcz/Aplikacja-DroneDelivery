using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SQLite;
using DroneDeliveryLinux.Models;

namespace DroneDeliveryLinux.Services;

public class DatabaseService
{
    private SQLiteAsyncConnection? _database;

    async Task Init()
    {
        if (_database is not null) return;

        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DroneDeliveryLinux", "DroneOrders.db3");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        _database = new SQLiteAsyncConnection(dbPath);
        await _database.CreateTableAsync<DroneOrder>();
    }

    public async Task<List<DroneOrder>> GetOrdersAsync()
    {
        await Init();
        return await _database!.Table<DroneOrder>().ToListAsync();
    }

    public async Task AddOrderAsync(DroneOrder order)
    {
        await Init();
        await _database!.InsertAsync(order);
    }

    public async Task UpdateOrderAsync(DroneOrder order)
    {
        await Init();
        await _database!.UpdateAsync(order);
    }
}
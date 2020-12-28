using System;
using System.Data.SQLite;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IpUpdater
{
    public class Worker : BackgroundService
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly HttpClient _httpClient;
        private readonly ILogger<Worker> _logger;
        private readonly string ipdst = "http://ipv4.icanhazip.com/";

        public Worker(IHostApplicationLifetime hostApplicationLifetime, ILogger<Worker> logger)
        {
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // PREP DATA
                var targetHost = Environment.GetEnvironmentVariable("IPUPD_TARGET_HOST") ?? "@";
                var domainName = Environment.GetEnvironmentVariable("IPUPD_DOMAIN_NAME");
                var ddnsPassword = Environment.GetEnvironmentVariable("IPUPD_DDNS_PWD");
                var ipAddress = await _httpClient.GetStringAsync(ipdst);
                var targetUrl =
                    $"https://dynamicdns.park-your-domain.com/update?host={targetHost}&domain={domainName}&password={ddnsPassword}&ip={ipAddress}";

                var ipChanged = false;
                var previousIp = "";

                // DATABASE PREPARATION
                await using var con = new SQLiteConnection(@"URI=file:ipupdater.db");
                con.Open();

                const string stm = "CREATE TABLE IF NOT EXISTS config(ip CHAR(15))";
                await using var cmd = new SQLiteCommand(stm, con);
                cmd.ExecuteNonQuery();

                // GET STORED DATA
                const string stm2 = "SELECT ip FROM config";
                await using var cmd2 = new SQLiteCommand(stm2, con);
                await using var rdr = cmd2.ExecuteReader();

                if (rdr.HasRows)
                {
                    rdr.Read();
                    previousIp = rdr.GetString(0);
                    ipChanged = previousIp != ipAddress;
                }
                else
                {
                    var stm3 = $"INSERT INTO config (ip) VALUES('{ipAddress}')";
                    await using var cmd3 = new SQLiteCommand(stm3, con);
                    cmd3.ExecuteNonQuery();
                    ipChanged = true;
                }

                // EXECUTE
                if (ipChanged)
                {
                    _logger.LogInformation("IP has changed. Attempting to update IP.");
                    // Update in db
                    var stm4 = $"UPDATE config SET ip = '{ipAddress}' WHERE ip = '{previousIp}'";
                    await using var cmd4 = new SQLiteCommand(stm4, con);
                    cmd4.ExecuteNonQuery();

                    // Send request to namecheap
                    var updateResponse = await _httpClient.GetStringAsync(targetUrl);
                    _logger.LogInformation(updateResponse);
                }
                else
                {
                    _logger.LogInformation("Nothing has changed. Ignoring attempts to update IP.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError("An error occured");
                _logger.LogError(e.ToString());
            }
            finally
            {
                _hostApplicationLifetime.StopApplication();
            }
        }
    }
}
using AllInOneExchangeHub.Common.ExchangeHub;
using AllInOneExchangeHub.DataAccessLayer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AllInOneExchangeHub.Common.Models;
using static AllInOneExchangeHub.Exchanges.Bitfinex.Response.Wallet;

namespace AllInOneExchangeHub.Exchanges.Bitfinex
{
    //Balance Data Class for API
    public partial class Bitfinex
    {
        public override async Task StoreBalancesHTTP()
        {
            try
            {

                string json = await HTTP.Post("v2/auth/r/wallets");
                if (json.Length < 10)
                {
                    return;
                }


                var serializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };

                List<List<object>> balanceData = JsonConvert.DeserializeObject<List<List<object>>>(json, serializerSettings);
                List<Models.BalanceItem> balances = new List<Models.BalanceItem>();

                foreach (var balanceItem in balanceData)
                {
                    try
                    {
                        string walletType = balanceItem[0].ToString();
                        string currency = ConvertBitfinexBaseOrTargetSymbol(balanceItem[1].ToString());
                        decimal total = Convert.ToDecimal(balanceItem[2]);
                        decimal unsettledInterest = Convert.ToDecimal(balanceItem[3]);
                        decimal availableBalance = Convert.ToDecimal(balanceItem[4]);
                        string lastChange = balanceItem[5]?.ToString(); // Note: Handle nullable values
                        Models.BalanceItem balance = new Models.BalanceItem()
                        {
                            Available = availableBalance,
                            Balance = total,
                            ExchangeTokenSymbol = currency,
                            BalanceType = walletType.ToUpper(),
                            Exchange = ExchangeType.BITFINEX.ToString(),
                            TimeReceived = DateTimeOffset.Now.ToUnixTimeMilliseconds()


                        };
                        balances.Add(balance);
                        if (walletType == "exchange")
                        {
                            //the above code used to be here
                        }
                    }
                    catch (Exception ex)
                    {

                        throw;
                    }
                }

                DataTable dataTable = new DataTable();
                dataTable = DBHelper.CreateDataTable(balances, "AIO_Exchange_Balances_Merge");
                DBManager.ClearBulkTable("AIO_Exchange_Balances_Merge", 2, "AIO_Exchange_Balances_MergeData", ExchangeType.BITFINEX.ToString());

                DBManager.BulkCopy(dataTable, "AIO_Exchange_Balances_Merge", 1, "AIO_Exchange_Balances_MergeData", ExchangeType.BITFINEX.ToString());

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);

            }
        }
        public override async Task<List<Models.BalanceItem>> GetBalancesHTTP()
        {
            List<Models.BalanceItem> balances = new List<Models.BalanceItem>();
            try
            {

                string json = await HTTP.Post("v2/auth/r/wallets");
                if (json.Length < 10)
                {
                    return balances;
                }


                var serializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                };

                List<List<object>> balanceData = JsonConvert.DeserializeObject<List<List<object>>>(json, serializerSettings);
                

                foreach (var balanceItem in balanceData)
                {
                    try
                    {
                        string walletType = balanceItem[0].ToString();
                        string currency = ConvertBitfinexBaseOrTargetSymbol(balanceItem[1].ToString());
                        decimal total = Convert.ToDecimal(balanceItem[2]);
                        decimal unsettledInterest = Convert.ToDecimal(balanceItem[3]);
                        decimal availableBalance = Convert.ToDecimal(balanceItem[4]);
                        string lastChange = balanceItem[5]?.ToString(); // Note: Handle nullable values
                        Models.BalanceItem balance = new Models.BalanceItem()
                        {
                            Available = availableBalance,
                            Balance = total,
                            ExchangeTokenSymbol = currency,
                            BalanceType = walletType.ToUpper(),
                            Exchange = ExchangeType.BITFINEX.ToString(),
                            TimeReceived = DateTimeOffset.Now.ToUnixTimeMilliseconds()


                        };
                        balances.Add(balance);
                        if (walletType == "exchange")
                        {
                            //the above code used to be here
                        }
                    }
                    catch (Exception ex)
                    {

                        throw;
                    }
                }



            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);

            }
            return balances;
        }
        
    }
}

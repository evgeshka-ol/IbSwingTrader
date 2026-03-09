using IBApi;
using IBApi.protobuf;
using Contract = IBApi.Contract;
using Order = IBApi.Order;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsConnection : EWrapper
    {
        private readonly EReaderSignal _signal;
        private readonly EClientSocket _client;
        private EReader? _reader;

        public TwsConnection()
        {
            _signal = new EReaderMonitorSignal();
            _client = new EClientSocket(this, _signal);
        }

        public void Connect(string host = "127.0.0.1", int port = 7496, int clientId = 1)
        {
            _client.eConnect(host, port, clientId);

            if (!_client.IsConnected())
                throw new Exception("Failed to connect to TWS");

            _reader = new EReader(_client, _signal);
            _reader.Start();

            new Thread(() =>
            {
                while (_client.IsConnected())
                {
                    _signal.waitForSignal();
                    _reader.processMsgs();
                }
            }).Start();
        }

        public bool IsConnected => _client.IsConnected();

        public EClientSocket Client => _client;

        private Contract CreateStock(string symbol)
        {
            return new Contract
            {
                Symbol = symbol,
                SecType = "STK",
                Exchange = "SMART",
                Currency = "USD"
            };
        }

        // ---- EWrapper methods ----

        public void error(Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }

        public void error(string str)
        {
            Console.WriteLine("Error: " + str);
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            Console.WriteLine($"IB Error {errorCode}: {errorMsg}");
        }

        public void connectionClosed()
        {
            Console.WriteLine("TWS connection closed");
        }

        public void nextValidId(int orderId)
        {
            Console.WriteLine($"Connected to TWS. Next OrderId: {orderId}");

            _client.reqCurrentTime();

            _client.reqHistoricalData(2, CreateStock("RIVN"), "", "30 D", "4 hours", "TRADES", 1, 1, false, null);
        }

        public void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            // ignore
        }

        public void currentTime(long time)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(time);
            Console.WriteLine($"Server time: {dt}");
        }

        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            // ignore
        }

        public void tickSize(int tickerId, int field, decimal size)
        {
            // ignore
        }

        public void tickString(int tickerId, int field, string value)
        {
            // ignore
        }

        public void tickGeneric(int tickerId, int field, double value)
        {
            // ignore
        }

        public void tickEFP(int tickerId, int tickType, double basisPoints, string formattedBasisPoints, double impliedFuture, int holdDays, string futureLastTradeDate, double dividendImpact, double dividendsToLastTradeDate)
        {
            // ignore
        }

        public void deltaNeutralValidation(int reqId, IBApi.DeltaNeutralContract deltaNeutralContract)
        {
            // ignore
        }

        public void tickOptionComputation(int tickerId, int field, int tickAttrib, double impliedVolatility, double delta, double optPrice, double pvDividend, double gamma, double vega, double theta, double undPrice)
        {
            // ignore
        }

        public void tickSnapshotEnd(int tickerId)
        {
            // ignore
        }

        public void managedAccounts(string accountsList)
        {
            // ignore
        }

        public void accountSummary(int reqId, string account, string tag, string value, string currency)
        {
            // ignore
        }

        public void accountSummaryEnd(int reqId)
        {
            // ignore
        }

        public void bondContractDetails(int reqId, ContractDetails contract)
        {
            // ignore
        }

        public void updateAccountValue(string key, string value, string currency, string accountName)
        {
            // ignore
        }

        public void updatePortfolio(IBApi.Contract contract, decimal position, double marketPrice, double marketValue, double averageCost, double unrealizedPNL, double realizedPNL, string accountName)
        {
            // ignore
        }

        public void updateAccountTime(string timestamp)
        {
            // ignore
        }

        public void accountDownloadEnd(string account)
        {
            // ignore
        }

        public void orderStatus(int orderId, string status, decimal filled, decimal remaining, double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId, string whyHeld, double mktCapPrice)
        {
            // ignore
        }

        public void openOrder(int orderId, IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            // ignore
        }

        public void openOrderEnd()
        {
            // ignore
        }

        public void contractDetails(int reqId, ContractDetails contractDetails)
        {
            // ignore
        }

        public void contractDetailsEnd(int reqId)
        {
            // ignore
        }

        public void execDetails(int reqId, IBApi.Contract contract, IBApi.Execution execution)
        {
            // ignore
        }

        public void execDetailsEnd(int reqId)
        {
            // ignore
        }

        public void commissionAndFeesReport(CommissionAndFeesReport commissionAndFeesReport)
        {
            // ignore
        }

        public void fundamentalData(int reqId, string data)
        {
            // ignore
        }

        public void historicalData(int reqId, Bar bar)
        {
            Console.WriteLine($"{bar.Time} O:{bar.Open} H:{bar.High} L:{bar.Low} C:{bar.Close} V:{bar.Volume}");
        }

        public void historicalDataUpdate(int reqId, Bar bar)
        {
            // ignore
        }

        public void historicalDataEnd(int reqId, string start, string end)
        {
            // ignore
        }

        public void marketDataType(int reqId, int marketDataType)
        {
            // ignore
        }

        public void updateMktDepth(int tickerId, int position, int operation, int side, double price, decimal size)
        {
            // ignore
        }

        public void updateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, decimal size, bool isSmartDepth)
        {
            // ignore
        }

        public void updateNewsBulletin(int msgId, int msgType, string message, string origExchange)
        {
            // ignore
        }

        public void position(string account, IBApi.Contract contract, decimal pos, double avgCost)
        {
            // ignore
        }

        public void positionEnd()
        {
            // ignore
        }

        public void realtimeBar(int reqId, long date, double open, double high, double low, double close, decimal volume, decimal WAP, int count)
        {
            // ignore
        }

        public void scannerParameters(string xml)
        {
            // ignore
        }

        public void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
        {
            // ignore
        }

        public void scannerDataEnd(int reqId)
        {
            // ignore
        }

        public void receiveFA(int faDataType, string faXmlData)
        {
            // ignore
        }

        public void verifyMessageAPI(string apiData)
        {
            // ignore
        }

        public void verifyCompleted(bool isSuccessful, string errorText)
        {
            // ignore
        }

        public void verifyAndAuthMessageAPI(string apiData, string xyzChallenge)
        {
            // ignore
        }

        public void verifyAndAuthCompleted(bool isSuccessful, string errorText)
        {
            // ignore
        }

        public void displayGroupList(int reqId, string groups)
        {
            // ignore
        }

        public void displayGroupUpdated(int reqId, string contractInfo)
        {
            // ignore
        }

        public void connectAck()
        {
            // ignore
        }

        public void positionMulti(int requestId, string account, string modelCode, IBApi.Contract contract, decimal pos, double avgCost)
        {
            // ignore
        }

        public void positionMultiEnd(int requestId)
        {
            // ignore
        }

        public void accountUpdateMulti(int requestId, string account, string modelCode, string key, string value, string currency)
        {
            // ignore
        }

        public void accountUpdateMultiEnd(int requestId)
        {
            // ignore
        }

        public void securityDefinitionOptionParameter(int reqId, string exchange, int underlyingConId, string tradingClass, string multiplier, HashSet<string> expirations, HashSet<double> strikes)
        {
            // ignore
        }

        public void securityDefinitionOptionParameterEnd(int reqId)
        {
            // ignore
        }

        public void softDollarTiers(int reqId, IBApi.SoftDollarTier[] tiers)
        {
            // ignore
        }

        public void familyCodes(FamilyCode[] familyCodes)
        {
            // ignore
        }

        public void symbolSamples(int reqId, ContractDescription[] contractDescriptions)
        {
            // ignore
        }

        public void mktDepthExchanges(DepthMktDataDescription[] depthMktDataDescriptions)
        {
            // ignore
        }

        public void tickNews(int tickerId, long timeStamp, string providerCode, string articleId, string headline, string extraData)
        {
            // ignore
        }

        public void smartComponents(int reqId, Dictionary<int, KeyValuePair<string, char>> theMap)
        {
            // ignore
        }

        public void tickReqParams(int tickerId, double minTick, string bboExchange, int snapshotPermissions)
        {
            // ignore
        }

        public void newsProviders(NewsProvider[] newsProviders)
        {
            // ignore
        }

        public void newsArticle(int requestId, int articleType, string articleText)
        {
            // ignore
        }

        public void historicalNews(int requestId, string time, string providerCode, string articleId, string headline)
        {
            // ignore
        }

        public void historicalNewsEnd(int requestId, bool hasMore)
        {
            // ignore
        }

        public void headTimestamp(int reqId, string headTimestamp)
        {
            // ignore
        }

        public void histogramData(int reqId, HistogramEntry[] data)
        {
            // ignore
        }

        public void rerouteMktDataReq(int reqId, int conId, string exchange)
        {
            // ignore
        }

        public void rerouteMktDepthReq(int reqId, int conId, string exchange)
        {
            // ignore
        }

        public void marketRule(int marketRuleId, PriceIncrement[] priceIncrements)
        {
            // ignore
        }

        public void pnl(int reqId, double dailyPnL, double unrealizedPnL, double realizedPnL)
        {
            // ignore
        }

        public void pnlSingle(int reqId, decimal pos, double dailyPnL, double unrealizedPnL, double realizedPnL, double value)
        {
            // ignore
        }

        public void historicalTicks(int reqId, HistoricalTick[] ticks, bool done)
        {
            // ignore
        }

        public void historicalTicksBidAsk(int reqId, HistoricalTickBidAsk[] ticks, bool done)
        {
            // ignore
        }

        public void historicalTicksLast(int reqId, HistoricalTickLast[] ticks, bool done)
        {
            // ignore
        }

        public void tickByTickAllLast(int reqId, int tickType, long time, double price, decimal size, TickAttribLast tickAttribLast, string exchange, string specialConditions)
        {
            // ignore
        }

        public void tickByTickBidAsk(int reqId, long time, double bidPrice, double askPrice, decimal bidSize, decimal askSize, TickAttribBidAsk tickAttribBidAsk)
        {
            // ignore
        }

        public void tickByTickMidPoint(int reqId, long time, double midPoint)
        {
            // ignore
        }

        public void orderBound(long permId, int clientId, int orderId)
        {
            // ignore
        }

        public void completedOrder(IBApi.Contract contract, IBApi.Order order, IBApi.OrderState orderState)
        {
            // ignore
        }

        public void completedOrdersEnd()
        {
            // ignore
        }

        public void replaceFAEnd(int reqId, string text)
        {
            // ignore
        }

        public void wshMetaData(int reqId, string dataJson)
        {
            // ignore
        }

        public void wshEventData(int reqId, string dataJson)
        {
            // ignore
        }

        public void historicalSchedule(int reqId, string startDateTime, string endDateTime, string timeZone, HistoricalSession[] sessions)
        {
            // ignore
        }

        public void userInfo(int reqId, string whiteBrandingId)
        {
            // ignore
        }

        public void currentTimeInMillis(long timeInMillis)
        {
            // ignore
        }

        public void orderStatusProtoBuf(OrderStatus orderStatusProto)
        {
            // ignore
        }

        public void openOrderProtoBuf(OpenOrder openOrderProto)
        {
            // ignore
        }

        public void openOrdersEndProtoBuf(OpenOrdersEnd openOrdersEndProto)
        {
            // ignore
        }

        public void errorProtoBuf(ErrorMessage errorMessageProto)
        {
            // ignore
        }

        public void execDetailsProtoBuf(ExecutionDetails executionDetailsProto)
        {
            // ignore
        }

        public void execDetailsEndProtoBuf(ExecutionDetailsEnd executionDetailsEndProto)
        {
            // ignore
        }
    }
}

using System.Collections.Concurrent;
using IBApi;
using IBApi.protobuf;
using IbSwingTrader.Extensions;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsConnection : EWrapper, ITwsConnection
    {
        private readonly EReaderMonitorSignal _signal;

        private readonly ITextLogger _logger;
        private EReader? _reader;

        private readonly SemaphoreSlim _historicalGate = new(3, 3);
        private readonly ConcurrentDictionary<int, List<Candle>> _buffers = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<List<Candle>>> _requests = new();

        private readonly ConcurrentDictionary<int, TaskCompletionSource<List<ContractDetails>>> _contractRequests = [];
        private readonly ConcurrentDictionary<int, List<ContractDetails>> _contractResults = [];

        private readonly ConcurrentDictionary<int, TaskCompletionSource<List<StockInfo>>> _scannerRequests = new();
        private readonly ConcurrentDictionary<int, List<StockInfo>> _scannerResults = new();

        private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _fundamentalRequests = new();

        private readonly ConcurrentDictionary<int, TaskCompletionSource<List<string>>> _marketProbeRequests = new();
        private readonly ConcurrentDictionary<int, List<string>> _marketProbeLogs = new();
        private readonly ConcurrentDictionary<int, string> _marketProbeSymbols = new();

        private int _nextRequestId = 1;
        private volatile bool _ibConnected = false;

        private TaskCompletionSource<string>? _scannerParametersTcs;

        public TwsConnection(ITextLogger logger)
        {
            _logger = logger;
            _signal = new EReaderMonitorSignal();
            Client = new EClientSocket(this, _signal);
        }

        public void Connect(string host = "127.0.0.1", int port = 7496, int clientId = 1)
        {
            Client.eConnect(host, port, clientId);

            if (!Client.IsConnected())
                throw new Exception("Failed to connect to TWS");

            _reader = new EReader(Client, _signal);
            _reader.Start();

            new Thread(() =>
            {
                while (Client.IsConnected())
                {
                    _signal.waitForSignal();
                    _reader.processMsgs();
                }
            })
            { IsBackground = true }.Start();
        }

        public bool IsConnected => Client.IsConnected();

        public EClientSocket Client { get; }

        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<List<Candle>> RequestHistoricalData(
            IBApi.Contract contract,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars)
        {
            await _historicalGate.WaitAsync();

            try
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var result = await RequestHistoricalDataOnce(
                        contract,
                        timeframe,
                        endTimeUtc,
                        bars,
                        attempt);

                    if (result.Count > 0)
                        return result;

                    if (attempt < 2)
                    {
                        _logger.Info($"Historical retry {attempt} for {contract.Symbol}");

                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                    }
                }

                return [];
            }
            finally
            {
                _historicalGate.Release();
            }
        }

        public async Task<List<ContractDetails>> GetContractDetails(IBApi.Contract contract)
        {
            await WaitForConnectionAsync();

            var requestId = Interlocked.Increment(ref _nextRequestId);

            var tcs = new TaskCompletionSource<List<ContractDetails>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _contractRequests[requestId] = tcs;

            Client.reqContractDetails(requestId, contract);

            return await tcs.Task;
        }

        public async Task<List<StockInfo>> GetStocksAsync(
            ScannerSubscription subscription,
            List<TagValue> filters)
        {
            await WaitForConnectionAsync();

            int requestId = Interlocked.Increment(ref _nextRequestId);

            var tcs = new TaskCompletionSource<List<StockInfo>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _scannerRequests[requestId] = tcs;
            _scannerResults[requestId] = [];

            _logger.Info($"Scanner request: {subscription.Instrument} {subscription.LocationCode} {subscription.ScanCode}");

            try
            {
                Client.reqScannerSubscription(
                    requestId,
                    subscription,
                    null,
                    filters);

                var result = await tcs.Task;
                _logger.Info($"Scanner completed. Items received: {result.Count}");
                return result;
            }
            finally
            {
                Client.cancelScannerSubscription(requestId);

                _scannerRequests.TryRemove(requestId, out _);
                _scannerResults.TryRemove(requestId, out _);
            }
        }

        public async Task<string> RequestScannerParametersAsync()
        {
            await WaitForConnectionAsync();

            var tcs = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _scannerParametersTcs = tcs;

            Client.reqScannerParameters();

            return await tcs.Task;
        }

        public async Task<FundamentalSnapshot?> GetFundamentalSnapshotAsync(IBApi.Contract contract)
        {
            await WaitForConnectionAsync();

            var requestId = Interlocked.Increment(ref _nextRequestId);

            var tcs = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _fundamentalRequests[requestId] = tcs;

            _logger.Info($"Fundamental request: {contract.Symbol}, report=ReportSnapshot");

            try
            {
                Client.reqFundamentalData(
                    requestId,
                    contract,
                    "ReportSnapshot",
                    null);

                var completed = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromSeconds(20)));

                if (completed != tcs.Task)
                {
                    _logger.Error($"Fundamental TIMEOUT: symbol={contract.Symbol}, reqId={requestId}");

                    try
                    {
                        Client.cancelFundamentalData(requestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Fundamental cancel failed: {ex.Message}");
                    }

                    return null;
                }

                var result = new FundamentalSnapshot { RawXml = await tcs.Task };

                if (string.IsNullOrWhiteSpace(result.RawXml))
                    return null;

                return result;
            }
            finally
            {
                _fundamentalRequests.TryRemove(requestId, out _);

                try
                {
                    Client.cancelFundamentalData(requestId);
                }
                catch
                {
                    // ignore
                }
            }
        }

        public async Task<List<string>> ProbeMarketDataAsync(IBApi.Contract contract, int seconds = 10)
        {
            await WaitForConnectionAsync();

            var reqId = Interlocked.Increment(ref _nextRequestId);

            var tcs = new TaskCompletionSource<List<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _marketProbeRequests[reqId] = tcs;
            _marketProbeLogs[reqId] = [];
            _marketProbeSymbols[reqId] = contract.Symbol ?? string.Empty;

            _logger.Info($"Market probe request: {contract.Symbol}, genericTicks=165,236,293,294,295");

            try
            {
                Client.reqMarketDataType(1); // live if available

                Client.reqMktData(
                    reqId,
                    contract,
                    "165,236,293,294,295",
                    false,
                    false,
                    null);

                var completed = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(TimeSpan.FromSeconds(seconds)));

                if (completed == tcs.Task)
                    return await tcs.Task;

                if (_marketProbeLogs.TryRemove(reqId, out var list))
                    return list;

                return [];
            }
            finally
            {
                try
                {
                    Client.cancelMktData(reqId);
                }
                catch
                {
                }

                _marketProbeRequests.TryRemove(reqId, out _);
                _marketProbeSymbols.TryRemove(reqId, out _);
            }
        }

        private async Task<List<Candle>> RequestHistoricalDataOnce(
            IBApi.Contract contract,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars,
            int attempt)
        {
            _logger.Info(
                $"GetCandles called for {contract.Symbol}, attempt {attempt}");

            var reqId = Interlocked.Increment(ref _nextRequestId);

            var endTime = endTimeUtc.ToIbEndTime();
            var duration = timeframe.ToIBDuration(bars);
            var barSize = timeframe.ToIBBarSize();
            var timeout = timeframe.GetHistoricalTimeout(bars);

            _logger.Info(
                $"Sending reqHistoricalData: symbol={contract.Symbol}, reqId={reqId}, end={endTime}, duration={duration}, barSize={barSize}, timeout={timeout.TotalSeconds}s");

            var tcs = new TaskCompletionSource<List<Candle>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _buffers[reqId] = [];
            _requests[reqId] = tcs;

            try
            {
                await WaitForConnectionAsync();

                Client.reqHistoricalData(
                    reqId,
                    contract,
                    endTime,
                    duration,
                    barSize,
                    "TRADES",
                    0,
                    1,
                    false,
                    null);

                _logger.Info($"REQ {reqId} waiting for candles");

                var completed = await Task.WhenAny(
                    tcs.Task,
                    Task.Delay(timeout));

                if (completed != tcs.Task)
                {
                    _logger.Error(
                        $"REQ {reqId} TIMEOUT: symbol={contract.Symbol}, duration={duration}, barSize={barSize}");

                    try
                    {
                        Client.cancelHistoricalData(reqId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"REQ {reqId} cancelHistoricalData failed: {ex.Message}");
                    }

                    return [];
                }

                var result = await tcs.Task;

                _logger.Info(
                    $"REQ {reqId} completed: symbol={contract.Symbol}, candles={result.Count}");

                return result;
            }
            finally
            {
                _buffers.TryRemove(reqId, out _);
                _requests.TryRemove(reqId, out _);
            }
        }

        private async Task WaitForConnectionAsync()
        {
            if (Client.IsConnected() && _ibConnected)
                return;

            if (!Client.IsConnected())
            {
                _logger.Info("Connecting to TWS...");
                Connect();
            }

            await Ready.Task;

            // Небольшая пауза после готовности, чтобы фермы успели стабилизироваться
            await Task.Delay(1500);
        }

        private void AddMarketProbeLine(int reqId, string line)
        {
            if (!_marketProbeLogs.TryGetValue(reqId, out var list))
                return;

            lock (list)
            {
                list.Add($"{DateTime.Now:HH:mm:ss.fff} | {line}");
            }
        }

        // ---- EWrapper methods ----

        public void error(Exception e)
        {
            _logger.Error($"IB EXCEPTION: {e}");
        }

        public void error(string str)
        {
            _logger.Error($"IB ERROR SHORT: {str}");
        }

        public void error(int id, int errorCode, string errorMsg)
        {
            _logger.Error($"IB ERROR LONG id={id} code={errorCode} msg={errorMsg}");
        }

        public void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
        {
            var details = string.IsNullOrWhiteSpace(advancedOrderRejectJson)
                ? string.Empty
                : $" details={advancedOrderRejectJson}";

            var header = "IB VERY LONG ERROR";
            var isError = true;

            switch (errorCode)
            {
                case 1100:
                    _ibConnected = false;
                    _logger.Error("IB connection lost");
                    break;

                case 1101:
                case 1102:
                    isError = false;
                    header = "IB VERY LONG INFO";
                    _ibConnected = true;
                    _logger.Info("IB connection restored");
                    break;

                case 2104:
                case 2106:
                case 2158:
                case 165:
                    isError = false;
                    header = "IB VERY LONG INFO";
                    break;
            }

            var noHistoricalData =
                errorCode == 162 &&
                errorMsg.Contains("HMDS query returned no data", StringComparison.OrdinalIgnoreCase);

            if (noHistoricalData)
            {
                isError = false;
                header = "IB VERY LONG INFO";
            }

            if (isError)
                _logger.Error($"{header} id={id} time={errorTime} code={errorCode} msg={errorMsg}{details}");
            else
                _logger.Info($"{header} id={id} time={errorTime} code={errorCode} msg={errorMsg}{details}");

            if (errorCode == 200)
            {
                if (_contractRequests.TryRemove(id, out var contractTcs))
                    contractTcs.TrySetException(new Exception($"Contract not found: {errorMsg}"));

                return;
            }

            if (noHistoricalData)
            {
                if (_requests.TryRemove(id, out var historyTcs))
                    historyTcs.TrySetResult([]);

                return;
            }

            if (_fundamentalRequests.TryRemove(id, out var fundamentalTcs))
            {
                fundamentalTcs.TrySetException(
                    new Exception($"Fundamental data request failed. code={errorCode}, msg={errorMsg}"));

                return;
            }

            if (_requests.TryRemove(id, out var requestTcs))
            {
                requestTcs.TrySetException(
                    new Exception($"Historical data request failed. code={errorCode}, msg={errorMsg}"));

                return;
            }

            if (_marketProbeRequests.TryRemove(id, out var probeTcs))
            {
                var lines = _marketProbeLogs.TryRemove(id, out var list)
                    ? list
                    : [];

                lines.Add($"ERROR code={errorCode} msg={errorMsg}");

                probeTcs.TrySetResult(lines);
                return;
            }
        }

        public void connectionClosed()
        {
            _ibConnected = false;
            _logger.Info("TWS connection closed");
        }

        public void nextValidId(int orderId)
        {
            _ibConnected = true;
            _logger.Info($"Connected to TWS. Next OrderId: {orderId}");
            Client.reqCurrentTime();
            Ready.TrySetResult(true);
        }

        public void currentTime(long time)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(time);
            _logger.Info($"Server time: {dt}");
        }

        public void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
        {
            AddMarketProbeLine(
                tickerId,
                $"tickPrice field={field} price={price} autoExec={attribs.CanAutoExecute} pastLimit={attribs.PastLimit} preOpen={attribs.PreOpen}");
        }

        public void tickSize(int tickerId, int field, decimal size)
        {
            AddMarketProbeLine(
                tickerId,
                $"tickSize field={field} size={size}");
        }

        public void tickString(int tickerId, int field, string value)
        {
            AddMarketProbeLine(
                tickerId,
                $"tickString field={field} value={value}");
        }

        public void tickGeneric(int tickerId, int field, double value)
        {
            AddMarketProbeLine(
                tickerId,
                $"tickGeneric field={field} value={value}");
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
            AddMarketProbeLine(tickerId, "tickSnapshotEnd");

            if (_marketProbeRequests.TryRemove(tickerId, out var tcs))
            {
                if (_marketProbeLogs.TryRemove(tickerId, out var list))
                    tcs.TrySetResult(list);
                else
                    tcs.TrySetResult([]);
            }
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
            if (!_contractResults.ContainsKey(reqId))
                _contractResults[reqId] = [];

            _contractResults[reqId].Add(contractDetails);
        }

        public void contractDetailsEnd(int reqId)
        {
            if (_contractRequests.TryGetValue(reqId, out var tcs))
            {
                var result = _contractResults.TryGetValue(reqId, out var list)
                    ? list
                    : [];

                tcs.SetResult(result);

                _contractRequests.TryRemove(reqId, out var contractTcs);
                _contractResults.TryRemove(reqId, out var contractDetails);
            }
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
            _logger.Info($"Fundamental data received: reqId={reqId}, size={data?.Length ?? 0}");

            if (_fundamentalRequests.TryRemove(reqId, out var tcs))
                tcs.TrySetResult(data ?? string.Empty);
        }

        public void historicalData(int reqId, Bar bar)
        {
            _logger.Debug($"bar {bar.Time}");

            if (!_buffers.TryGetValue(reqId, out var list))
                return;

            var candle = new Candle
            {
                Time = TwsTimeParser.ParseToUtc(bar.Time),
                Open = (decimal)bar.Open,
                High = (decimal)bar.High,
                Low = (decimal)bar.Low,
                Close = (decimal)bar.Close,
                Volume = bar.Volume
            };

            list.Add(candle);
        }

        public void historicalDataUpdate(int reqId, Bar bar)
        {
            // ignore
        }

        public void historicalDataEnd(int reqId, string start, string end)
        {
            if (!_requests.TryRemove(reqId, out var tcs))
                return;

            var candles = _buffers.TryRemove(reqId, out var list)
                ? list
                : [];

            tcs.SetResult(candles);
            _logger.Info($"END {reqId}");
        }

        public void marketDataType(int reqId, int marketDataType)
        {
            AddMarketProbeLine(
                reqId,
                $"marketDataType type={marketDataType}");
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
            _logger.Info("Scanner parameters received");

            _scannerParametersTcs?.TrySetResult(xml);
        }

        public void scannerData(int reqId, int rank, ContractDetails contractDetails, string distance, string benchmark, string projection, string legsStr)
        {
            if (!_scannerResults.TryGetValue(reqId, out var list))
                return;

            var contract = contractDetails.Contract;

            var stock = new StockInfo
            {
                Ticker = contract.Symbol ?? "",
                ConId = contract.ConId,
                Exchange = contract.Exchange ?? "",
                Currency = contract.Currency ?? "",
                TradingClass = contract.TradingClass ?? "",
                StockType = contract.SecType ?? "",
                Rank = rank
            };

            if (!list.Any(x => x.Ticker == stock.Ticker))
                list.Add(stock);
        }

        public void scannerDataEnd(int reqId)
        {
            if (_scannerRequests.TryGetValue(reqId, out var tcs) &&
                _scannerResults.TryGetValue(reqId, out var list))
            {
                tcs.TrySetResult(list);
            }
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

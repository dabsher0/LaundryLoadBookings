using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IBM.Data.DB2.iSeries;
using System.Data.SqlClient;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.Mail;

namespace LaundryLoadBookings
{
    class Program
    {
        public const string iseriesLibrary = "MGPRDDTA";
        static void Main(string[] args)
        {
            CancelPreviousBookings();
            UploadOrders(3);
        }

        static void CancelPreviousBookings()
        {
            List<string> bookings = new List<string>();

            try
            {
                iDB2Connection iSeriesConnection = new iDB2Connection("DataSource = 10.1.4.1; UserID=CRYSTAL; Password = CRYSTAL");
                iSeriesConnection.Open();

                iDB2Command iCmdReadBookingData = new iDB2Command("SELECT BOHP.CO407F AS BOOKINGNO FROM MGPRDDTA.BOHP BOHP INNER JOIN MGPRDDTA.BOCP BOCP ON BOHP.CO407F = BOCP.CO407F WHERE BOHP.CO408D LIKE 'Laundry Bookings%' AND BOHP.CO408F NOT IN ('2','3') Group By BOHP.CO407F", iSeriesConnection);
                iDB2DataReader iDrReadBookingData = iCmdReadBookingData.ExecuteReader();

                while (iDrReadBookingData.Read())
                {
                    string bookingNumber = iDrReadBookingData.GetValue(0).ToString();
                    bookings.Add(bookingNumber);
                }

                foreach (var booking in bookings)
                {
                    iDB2Command iCmdUpdateBOCPData = new iDB2Command("UPDATE MGPRDDTA.BOCP BOCP SET BOCP.CO408F = '2', BOCP.CO408O = @cancelledDate WHERE BOCP.CO407F = @bookingNumber AND BOCP.CO408F <> '3'", iSeriesConnection);
                    iCmdUpdateBOCPData.DeriveParameters();
                    iCmdUpdateBOCPData.Parameters["@bookingNumber"].Value = booking;
                    iCmdUpdateBOCPData.Parameters["@cancelledDate"].Value = DateTime.Now;
                    iCmdUpdateBOCPData.ExecuteNonQuery();
                }

                iSeriesConnection.Close();
            }
            catch (Exception ex)
            {
                new MailMessage();
                var smtpServer = new SmtpClient();
                smtpServer.Credentials = new System.Net.NetworkCredential("e.delivery@teammodern.com", "Deli157#2");
                smtpServer.Host = "mail.teammodern.com";
                smtpServer.EnableSsl = true;
                var mail = new MailMessage();
                mail.From = new MailAddress("e.delivery@teammodern.com", "Electronic Delivery", System.Text.Encoding.UTF8);
                mail.To.Add("d.absher@teammodern.com");
                mail.Subject = "Laundry Load Bookings - Cancel Previous Bookings";
                mail.Body = ex.ToString();
                mail.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;
                mail.ReplyToList.Add("d.absher@teammodern.com");
                smtpServer.Send(mail);
            }
        }

        static void UploadOrders(int source)
        {
            Dictionary<string, int?> loads = new Dictionary<string, int?>();
            List<string> items = new List<string>();
            List<ItemModel> itemModelList = new List<ItemModel>();
            eInvoiceEntities db = new eInvoiceEntities();
            SummerPromoEntities dbsp = new SummerPromoEntities();
            Dictionary<int?, string> loadDates = new Dictionary<int?, string>();

            iDB2Connection iSeriesConnection = new iDB2Connection("DataSource = 10.1.4.1; UserID=CRYSTAL; Password = CRYSTAL; DataCompression=True;");
            iSeriesConnection.Open();

            var allLoads = from l in dbsp.LaundryCustomers
                           where l.Load != null
                           select l;

            foreach (var currentLoad in allLoads)
            {
                loads.Add(currentLoad.CustomerNo, currentLoad.Load);
            }

           var orders = from ord in dbsp.Laundries
                         where ord.Qty > 0
                         select ord;

            foreach (var order in orders)
            {
                if (!items.Contains(order.ItemNo))
                {
                    items.Add(order.ItemNo);
                }
            }

            foreach (var item in items)
            {
                iDB2Command iCmdTsBkOrdrItems = new iDB2Command("SELECT ITBP.IBITCD AS ITEMNUMBER, ITBP.IBRPK AS REPACKFACTOR, ITBP.IBA2CT AS BUYER, ITBP.IBBMC7 AS VENDOR FROM MGPRDDTA.ITBP ITBP WHERE ITBP.IBITCD = @ITEMNUMBER", iSeriesConnection);
                iCmdTsBkOrdrItems.DeriveParameters();
                iCmdTsBkOrdrItems.Parameters["@ITEMNUMBER"].Value = item;
                iDB2DataReader iDrTsBkOrdrItems = iCmdTsBkOrdrItems.ExecuteReader();

                while (iDrTsBkOrdrItems.Read())
                {
                    ItemModel itemModel = new ItemModel();
                    itemModel.ItemNumber = item;
                    itemModel.RepackFactor = Convert.ToInt32(iDrTsBkOrdrItems.GetValue(1));
                    itemModel.Buyer = iDrTsBkOrdrItems.GetValue(2).ToString();
                    itemModel.Vendor = iDrTsBkOrdrItems.GetValue(3).ToString().Trim();

                    itemModelList.Add(itemModel);
                }
            }

            iDB2Command iCmdGetLoads = new iDB2Command("SELECT LWSP.LOADNO AS LOADNO, LWSP.DATE AS DATE FROM MDCUSTOM.LWSPDATE LWSP", iSeriesConnection);
            iDB2DataReader iDrGetLoads = iCmdGetLoads.ExecuteReader();

            while (iDrGetLoads.Read())
            {
                string load = iDrGetLoads.GetValue(0).ToString();
                string date = iDrGetLoads.GetValue(1).ToString();

                loadDates.Add(Convert.ToInt32(load), date);
            }



            db.Truncate_Booking_Order_Tables();

            int LineNo;
            int SequenceNo;

            

            iDB2Command iCmd = new iDB2Command("SELECT Max(CO407F) FROM " + iseriesLibrary + ".BOHP", iSeriesConnection);
            Int32 nextBookingOrderNo = Convert.ToInt32(iCmd.ExecuteScalar()) + 1;

            using (SqlConnection intranetSqlConnection = new SqlConnection("Data Source=SERVER-MD7;Initial Catalog=eInvoice;User Id=program_admin;Password=Modernadmin1"))
            {
                intranetSqlConnection.Open();

                SqlCommand sqlBohpCmd = new SqlCommand();
                sqlBohpCmd.Connection = intranetSqlConnection;

                SqlCommand sqlBodpCmd = new SqlCommand();
                sqlBodpCmd.Connection = intranetSqlConnection;

                SqlCommand sqlBoipCmd = new SqlCommand();
                sqlBoipCmd.Connection = intranetSqlConnection;

                SqlCommand sqlBocpCmd = new SqlCommand();
                sqlBocpCmd.Connection = intranetSqlConnection;
                

                SqlCommand sqlUpdateBocpSeqNoCmd = new SqlCommand();
                sqlUpdateBocpSeqNoCmd.Connection = intranetSqlConnection;

                sqlBohpCmd.CommandText = ("INSERT INTO Tbl_BOHP Values (@BookingOrderNo, @Description, @Reference, @BookingOrderType, @ShipWithOrder, @ShipAllOrNone, @SequenceType, @Frequency, @Duration, @DistributionCenter, @Load, @ScheduledDeliveryDate, @BookingOrderStatus, @DateCompleted, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)");
                sqlBohpCmd.Parameters.AddWithValue("@BookingOrderNo", nextBookingOrderNo);
                sqlBohpCmd.Parameters.AddWithValue("@Description", " ");
                sqlBohpCmd.Parameters.AddWithValue("@Reference", " ");
                sqlBohpCmd.Parameters.AddWithValue("@BookingOrderType", "1");
                sqlBohpCmd.Parameters.AddWithValue("@ShipWithOrder", "0");
                sqlBohpCmd.Parameters.AddWithValue("@ShipAllOrNone", "1");
                sqlBohpCmd.Parameters.AddWithValue("@SequenceType", "1");
                sqlBohpCmd.Parameters.AddWithValue("@Frequency", "3");
                sqlBohpCmd.Parameters.AddWithValue("@Duration", 0);
                sqlBohpCmd.Parameters.AddWithValue("@DistributionCenter", " ");
                sqlBohpCmd.Parameters.AddWithValue("@Load", 0);
                sqlBohpCmd.Parameters.AddWithValue("@ScheduledDeliveryDate", 0);
                sqlBohpCmd.Parameters.AddWithValue("@BookingOrderStatus", "1");
                sqlBohpCmd.Parameters.AddWithValue("@DateCompleted", DateTime.Now);
                sqlBohpCmd.Parameters.AddWithValue("@CreationDate", DateTime.Now);
                sqlBohpCmd.Parameters.AddWithValue("@CreationTime", DateTime.Now);
                sqlBohpCmd.Parameters.AddWithValue("@LastChangeDate", DateTime.Now);
                sqlBohpCmd.Parameters.AddWithValue("@LastChangeTime", DateTime.Now);
                sqlBohpCmd.Parameters.AddWithValue("@LastChangeUserId", "Intranet");
                sqlBohpCmd.Parameters.AddWithValue("@LastChangeFunction", "Intranet");
                sqlBohpCmd.Parameters.AddWithValue("@RecordStatus", "1");

                sqlBodpCmd.CommandText = ("INSERT INTO Tbl_BODP Values (@BookingOrderNo, @FirstShipDate, @LastOrderDate, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)");
                sqlBodpCmd.Parameters.Clear();
                sqlBodpCmd.Parameters.AddWithValue("@BookingOrderNo", nextBookingOrderNo);
                sqlBodpCmd.Parameters.AddWithValue("@FirstShipDate", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@LastOrderDate", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@CreationDate", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@CreationTime", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@LastChangeDate", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@LastChangeTime", DateTime.Now);
                sqlBodpCmd.Parameters.AddWithValue("@LastChangeUserId", "Intranet");
                sqlBodpCmd.Parameters.AddWithValue("@LastChangeFunction", "Intranet");
                sqlBodpCmd.Parameters.AddWithValue("@RecordStatus", "1");

                sqlBoipCmd.CommandText = ("INSERT INTO Tbl_BOIP Values (@BookingOrderNo, @EnumeratedItem, @RepackFactor, @ItemPackNo, @OrderQuantity, @FromProtected, @ProtectDate, @PriceOverride, @LineNumber, @Vendor, @PONumber, @POLevel, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)");
                sqlBoipCmd.Parameters.AddWithValue("@BookingOrderNo", nextBookingOrderNo);
                sqlBoipCmd.Parameters.AddWithValue("@EnumeratedItem", "000000" + "00001");
                sqlBoipCmd.Parameters.AddWithValue("@RepackFactor", 1);
                sqlBoipCmd.Parameters.AddWithValue("@ItemPackNo", "000000");
                sqlBoipCmd.Parameters.AddWithValue("@OrderQuantity", 0);
                sqlBoipCmd.Parameters.AddWithValue("@FromProtected", "0");
                sqlBoipCmd.Parameters.AddWithValue("@ProtectDate", DateTime.Now);
                sqlBoipCmd.Parameters.AddWithValue("@PriceOverride", 0);
                sqlBoipCmd.Parameters.AddWithValue("@LineNumber", 0);
                sqlBoipCmd.Parameters.AddWithValue("@Vendor", " ");
                sqlBoipCmd.Parameters.AddWithValue("@PONumber", 0);
                sqlBoipCmd.Parameters.AddWithValue("@POLevel", 0);
                sqlBoipCmd.Parameters.AddWithValue("@CreationDate", DateTime.Now);
                sqlBoipCmd.Parameters.AddWithValue("@CreationTime", DateTime.Now);
                sqlBoipCmd.Parameters.AddWithValue("@LastChangeDate", DateTime.Now);
                sqlBoipCmd.Parameters.AddWithValue("@LastChangeTime", DateTime.Now);
                sqlBoipCmd.Parameters.AddWithValue("@LastChangeUserId", "Intranet");
                sqlBoipCmd.Parameters.AddWithValue("@LastChangeFunction", "Intranet");
                sqlBoipCmd.Parameters.AddWithValue("@RecordStatus", "1");


                sqlBocpCmd.CommandText = ("INSERT INTO Tbl_BOCP Values (@BookingOrderNo, @GroupNo, @CustomerNo, @BusinessType, @FirstShipDate, @SequenceNo, @CustomerPoNo, @BookingOrderType, @EnumeratedItem, @RepackFactor, @ItemPackNo, @ShipWithOrder, @OriginalOrderQty, @OrderQty, @FromProtected, @ProtectDate, @QtyShipped, @QtyCancelled, @DateCancelled, @PriceOverride, @Salesperson, @DistributionCenter, @Load, @ScheduledDeliveryDate, @Vendor, @Buyer, @PONumber, @POLevel, @QuantityOnOrder, @BookingOrderStatus, @OpenBookingsUpdate, @DateCompleted, @LineNumber, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)");
                sqlBocpCmd.Parameters.AddWithValue("@BookingOrderNo", nextBookingOrderNo);
                sqlBocpCmd.Parameters.AddWithValue("@GroupNo", " ");
                sqlBocpCmd.Parameters.AddWithValue("@CustomerNo", " ");
                sqlBocpCmd.Parameters.AddWithValue("@BusinessType", "1");
                sqlBocpCmd.Parameters.AddWithValue("@FirstShipDate", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@SequenceNo", 0);
                sqlBocpCmd.Parameters.AddWithValue("@CustomerPoNo", "Laundry");
                sqlBocpCmd.Parameters.AddWithValue("@BookingOrderType", "2");
                sqlBocpCmd.Parameters.AddWithValue("@EnumeratedItem", " ");
                sqlBocpCmd.Parameters.AddWithValue("@RepackFactor", 1);
                sqlBocpCmd.Parameters.AddWithValue("@ItemPackNo", " ");
                sqlBocpCmd.Parameters.AddWithValue("@ShipWithOrder", "0");
                sqlBocpCmd.Parameters.AddWithValue("@OriginalOrderQty", 0);
                sqlBocpCmd.Parameters.AddWithValue("@OrderQty", 0);
                sqlBocpCmd.Parameters.AddWithValue("@FromProtected", "0");
                sqlBocpCmd.Parameters.AddWithValue("@ProtectDate", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@QtyShipped", 0);
                sqlBocpCmd.Parameters.AddWithValue("@QtyCancelled", 0);
                sqlBocpCmd.Parameters.AddWithValue("@DateCancelled", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@PriceOverride", 0);
                sqlBocpCmd.Parameters.AddWithValue("@Salesperson", " ");
                sqlBocpCmd.Parameters.AddWithValue("@DistributionCenter", "MODERN");
                sqlBocpCmd.Parameters.AddWithValue("@Load", 0);
                sqlBocpCmd.Parameters.AddWithValue("@ScheduledDeliveryDate", 0);
                sqlBocpCmd.Parameters.AddWithValue("@Vendor", " ");
                sqlBocpCmd.Parameters.AddWithValue("@Buyer", "02");
                sqlBocpCmd.Parameters.AddWithValue("@PONumber", 0);
                sqlBocpCmd.Parameters.AddWithValue("@POLevel", 0);
                sqlBocpCmd.Parameters.AddWithValue("@QuantityOnOrder", 0);
                sqlBocpCmd.Parameters.AddWithValue("@BookingOrderStatus", "1");
                sqlBocpCmd.Parameters.AddWithValue("@OpenBookingsUpdate", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@DateCompleted", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@LineNumber", 0);
                sqlBocpCmd.Parameters.AddWithValue("@CreationDate", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@CreationTime", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@LastChangeDate", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@LastChangeTime", DateTime.Now);
                sqlBocpCmd.Parameters.AddWithValue("@LastChangeUserId", "Intranet");
                sqlBocpCmd.Parameters.AddWithValue("@LastChangeFunction", "Intranet");
                sqlBocpCmd.Parameters.AddWithValue("@RecordStatus", "1");

                //while (iDrTsBkOrdrDesc.Read())
                //{
                sqlBohpCmd.Parameters["@BookingOrderNo"].Value = nextBookingOrderNo;
                //sqlBohpCmd.Parameters["@Description"].Value = iDrTsBkOrdrDesc.GetValue(0);
                if (source == 0)
                {
                    sqlBohpCmd.Parameters["@Description"].Value = "Rep Book - ASSOCIATED DIS";
                }
                else if (source == 1)
                {
                    sqlBohpCmd.Parameters["@Description"].Value = "WAM Pro Upload " + DateTime.Now.ToString("MM-dd-yy");
                }
                else if (source == 2)
                {
                    sqlBohpCmd.Parameters["@Description"].Value = "Rep Book-SWEDISH " + DateTime.Now.ToString("MM-dd-yy");
                }
                else if (source == 3)
                {
                    sqlBohpCmd.Parameters["@Description"].Value = "Laundry Bookings " + DateTime.Now.ToString("MM-dd-yy");
                }
                sqlBohpCmd.ExecuteNonQuery();

                //VendorNo = iDrTsBkOrdrDesc.GetValue(1).ToString();
                //iCmdTsBkOrdrDates.Parameters["@ParmVendorNo"].Value = VendorNo;
                //iDB2DataReader iDrTsBkOrdrDates = iCmdTsBkOrdrDates.ExecuteReader();

                //while (iDrTsBkOrdrDates.Read())
                //{
                    sqlBodpCmd.Parameters["@BookingOrderNo"].Value = nextBookingOrderNo;
                    sqlBodpCmd.Parameters["@FirstShipDate"].Value = DateTime.Now.AddDays(3);
                    sqlBodpCmd.Parameters["@LastOrderDate"].Value = DateTime.Now.AddDays(123);
                    sqlBodpCmd.ExecuteNonQuery();
                //}

                //iDrTsBkOrdrDates.Close();

                //iCmdTsBkOrdrItems.Parameters["@ParmVendorNo"].Value = VendorNo;
                

                LineNo = 0;
                //while (iDrTsBkOrdrItems.Read())
                //{
                foreach (var item in items)
                {
                    iDB2Command iCmdTsBkOrdrItems = new iDB2Command("SELECT ITBP.IBITM, ITBP.IBRPK, ITBP.IBITCD, ITBP.IBBMC7 FROM MGPRDDTA.ITBP ITBP WHERE ITBP.IBITCD = @ItemNumber", iSeriesConnection);
                    iCmdTsBkOrdrItems.DeriveParameters();
                    iCmdTsBkOrdrItems.Parameters["@ItemNumber"].Value = item;
                    iDB2DataReader iDrTsBkOrdrItems = iCmdTsBkOrdrItems.ExecuteReader();

                    while (iDrTsBkOrdrItems.Read())
                    {
                        LineNo++;
                        sqlBoipCmd.Parameters["@BookingOrderNo"].Value = nextBookingOrderNo;
                        sqlBoipCmd.Parameters["@LineNumber"].Value = LineNo;
                        //sqlBoipCmd.Parameters["@Vendor"].Value = VendorNo;
                        sqlBoipCmd.Parameters["@Vendor"].Value = iDrTsBkOrdrItems.GetValue(3);
                        sqlBoipCmd.Parameters["@EnumeratedItem"].Value = iDrTsBkOrdrItems.GetValue(0);
                        sqlBoipCmd.Parameters["@RepackFactor"].Value = iDrTsBkOrdrItems.GetValue(1);
                        sqlBoipCmd.Parameters["@ItemPackNo"].Value = iDrTsBkOrdrItems.GetValue(2);
                        sqlBoipCmd.ExecuteNonQuery();
                    }

                    iDrTsBkOrdrItems.Close();
                }
                //}

                

                //iCmdTsBkOrdrCustomers.Parameters["@ParmVendorNo"].Value = VendorNo;

                //iCmdTsBkOrdrCustomers.CommandTimeout = 0;
                //iDB2DataReader iDrTsBkOrdrCustomers = iCmdTsBkOrdrCustomers.ExecuteReader();

                SequenceNo = 0;
                //while (iDrTsBkOrdrCustomers.Read())
                //{
                foreach (var order in orders)
                {
                    iDB2Command iCmdTsBkOrdrCustomers = new iDB2Command("SELECT CUSP.AXNANB FROM MGPRDDTA.CUSP CUSP WHERE CUSP.AXMWNB = @CUSTOMER", iSeriesConnection);
                    iCmdTsBkOrdrCustomers.DeriveParameters();
                    iCmdTsBkOrdrCustomers.Parameters["@CUSTOMER"].Value = order.CustomerNo;
                    iCmdTsBkOrdrCustomers.CommandTimeout = 0;
                    iDB2DataReader iDrTsBkOrdrCustomers = iCmdTsBkOrdrCustomers.ExecuteReader();

                    while (iDrTsBkOrdrCustomers.Read())
                    {
                        var foundItem = itemModelList.FirstOrDefault(i => i.ItemNumber.Equals(order.ItemNo));

                        SequenceNo++;
                        sqlBocpCmd.Parameters["@BookingOrderNo"].Value = nextBookingOrderNo;
                        sqlBocpCmd.Parameters["@GroupNo"].Value = iDrTsBkOrdrCustomers.GetValue(0);
                        sqlBocpCmd.Parameters["@CustomerNo"].Value = order.CustomerNo;
                        sqlBocpCmd.Parameters["@SequenceNo"].Value = SequenceNo;
                        sqlBocpCmd.Parameters["@EnumeratedItem"].Value = order.ItemNo + "00001";
                        sqlBocpCmd.Parameters["@RepackFactor"].Value = foundItem.RepackFactor;
                        sqlBocpCmd.Parameters["@ItemPackNo"].Value = order.ItemNo;
                        //sqlBocpCmd.Parameters["@Salesperson"].Value = iDrTsBkOrdrCustomers.GetValue(5);

                        if (loads.ContainsKey(order.CustomerNo))
                        {
                            if (loadDates.ContainsKey(loads[order.CustomerNo]))
                            {
                                sqlBocpCmd.Parameters["@Load"].Value = loads[order.CustomerNo];
                                sqlBocpCmd.Parameters["@ScheduledDeliveryDate"].Value = loadDates[loads[order.CustomerNo]];
                            }
                        }

                        sqlBocpCmd.Parameters["@FirstShipDate"].Value = DateTime.Now.AddDays(3).ToString("yyyy-MM-dd");
                        sqlBocpCmd.Parameters["@OriginalOrderQty"].Value = order.Qty;
                        sqlBocpCmd.Parameters["@OrderQty"].Value = order.Qty;
                        sqlBocpCmd.Parameters["@Buyer"].Value = foundItem.Buyer;
                        sqlBocpCmd.Parameters["@Vendor"].Value = foundItem.Vendor;
                        sqlBocpCmd.ExecuteNonQuery();
                    }

                    iDrTsBkOrdrCustomers.Close();
                }
                //}

                

                nextBookingOrderNo++;

                //}

                //sqlUpdateBocpSeqNoCmd.CommandText = ("UPDATE Tbl_BOCP SET CO403N = Tbl_BOIP.CO408C FROM Tbl_BOCP INNER JOIN Tbl_BOIP ON Tbl_BOCP.CO1TA = Tbl_BOIP.CO1TA");
                //sqlUpdateBocpSeqNoCmd.ExecuteNonQuery();

                iCmd = new iDB2Command("UPDATE " + iseriesLibrary + ".MBKEYP SET MBFEKY = " + nextBookingOrderNo.ToString() + " WHERE MBFNID = 'DSH'", iSeriesConnection);
                iCmd.ExecuteNonQuery();
            }

            const string iMdsNullDate = "0001-01-01";
            const string iMdsNullTime = "00.00.00";

            iCmd = new iDB2Command("INSERT INTO " + iseriesLibrary + ".BOHP VALUES(@BookingOrderNo, @Description, @Reference, @BookingOrderType, @ShipWithOrder, @ShipAllOrNone, @SequenceType, @Frequency, @Duration, @DistributionCenter, @Load, @SchedDeliveryDate, @BookingOrderStatus, @DateCompleted, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)", iSeriesConnection);
            iCmd.DeriveParameters();



            var Tbl_bohp = from h in db.Tbl_BOHP
                           select h;

            foreach (var bohp in Tbl_bohp)
            {
                iCmd.Parameters["@BookingOrderNo"].Value = bohp.CO407F;
                iCmd.Parameters["@Description"].Value = bohp.CO408D;
                iCmd.Parameters["@Reference"].Value = bohp.CO408E;
                iCmd.Parameters["@BookingOrderType"].Value = bohp.CO408I;
                iCmd.Parameters["@ShipWithOrder"].Value = bohp.CO408H;
                iCmd.Parameters["@ShipAllOrNone"].Value = bohp.CO408T;
                iCmd.Parameters["@SequenceType"].Value = bohp.CO9992;
                iCmd.Parameters["@Frequency"].Value = bohp.CO408U;
                iCmd.Parameters["@Duration"].Value = bohp.CO408V;
                iCmd.Parameters["@DistributionCenter"].Value = bohp.CO2A;
                iCmd.Parameters["@Load"].Value = bohp.CO408W;
                iCmd.Parameters["@SchedDeliveryDate"].Value = bohp.CO409V;
                iCmd.Parameters["@BookingOrderStatus"].Value = bohp.CO408F;
                iCmd.Parameters["@DateCompleted"].Value = iMdsNullDate;
                iCmd.Parameters["@CreationDate"].Value = bohp.OBF002;
                iCmd.Parameters["@CreationTime"].Value = bohp.OBF003;
                iCmd.Parameters["@LastChangeDate"].Value = iMdsNullDate;
                iCmd.Parameters["@LastChangeTime"].Value = iMdsNullTime;
                iCmd.Parameters["@LastChangeUserId"].Value = bohp.MBFUID;
                iCmd.Parameters["@LastChangeFunction"].Value = bohp.MBFFID;
                iCmd.Parameters["@RecordStatus"].Value = bohp.MBFRCS;
                iCmd.ExecuteNonQuery();
            }


            iCmd = new iDB2Command("INSERT INTO " + iseriesLibrary + ".BHEP VALUES(@BookingOrderNo, @LeadTimeDays, @ExcludeSpiff, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)", iSeriesConnection);
            iCmd.DeriveParameters();

            var Tbl_bhep = from h in db.Tbl_BOHP
                           select h;

            foreach (var bohp in Tbl_bhep)
            {
                iCmd.Parameters["@BookingOrderNo"].Value = bohp.CO407F;
                iCmd.Parameters["@LeadTimeDays"].Value = 0;
                iCmd.Parameters["@ExcludeSpiff"].Value = 1;
                iCmd.Parameters["@CreationDate"].Value = bohp.OBF002;
                iCmd.Parameters["@CreationTime"].Value = bohp.OBF003;
                iCmd.Parameters["@LastChangeDate"].Value = iMdsNullDate;
                iCmd.Parameters["@LastChangeTime"].Value = iMdsNullTime;
                iCmd.Parameters["@LastChangeUserId"].Value = bohp.MBFUID;
                iCmd.Parameters["@LastChangeFunction"].Value = bohp.MBFFID;
                iCmd.Parameters["@RecordStatus"].Value = bohp.MBFRCS;
                iCmd.ExecuteNonQuery();
            }

            iCmd = new iDB2Command("INSERT INTO " + iseriesLibrary + ".BODP VALUES(@BookingOrderNo, @FirstShipDate, @LastOrderDate, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)", iSeriesConnection);
            iCmd.DeriveParameters();

            var Tbl_bodp = from d in db.Tbl_BODP
                           select d;

            foreach (var bodp in Tbl_bodp)
            {
                iCmd.Parameters["@BookingOrderNo"].Value = bodp.CO407F;
                iCmd.Parameters["@FirstShipDate"].Value = bodp.CO408N;
                iCmd.Parameters["@LastOrderDate"].Value = bodp.CO409U;
                iCmd.Parameters["@CreationDate"].Value = bodp.OBF002;
                iCmd.Parameters["@CreationTime"].Value = bodp.OBF003;
                iCmd.Parameters["@LastChangeDate"].Value = iMdsNullDate;
                iCmd.Parameters["@LastChangeTime"].Value = iMdsNullTime;
                iCmd.Parameters["@LastChangeUserId"].Value = bodp.MBFUID;
                iCmd.Parameters["@LastChangeFunction"].Value = bodp.MBFFID;
                iCmd.Parameters["@RecordStatus"].Value = bodp.MBFRCS;
                iCmd.ExecuteNonQuery();
            }

            iCmd = new iDB2Command("INSERT INTO " + iseriesLibrary + ".BOIP VALUES(@BookingOrderNo, @EnumeratedItem, @RepackFactor, @ItemPackNo, @OrderQuantity, @FromProtected, @ProtectDate, @PriceOverride, @LineNumber, @Vendor, @PONumber, @POLevel, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)", iSeriesConnection);
            iCmd.DeriveParameters();

            var Tbl_boip = from i in db.Tbl_BOIP
                           select i;

            foreach (var boip in Tbl_boip)
            {
                iCmd.Parameters["@BookingOrderNo"].Value = boip.CO407F;
                iCmd.Parameters["@EnumeratedItem"].Value = boip.CO1UA;
                iCmd.Parameters["@RepackFactor"].Value = boip.CO1ZA;
                iCmd.Parameters["@ItemPackNo"].Value = boip.CO1TA;
                iCmd.Parameters["@OrderQuantity"].Value = boip.CO408Z;
                iCmd.Parameters["@FromProtected"].Value = boip.CO408L;
                iCmd.Parameters["@ProtectDate"].Value = iMdsNullDate;
                iCmd.Parameters["@PriceOverride"].Value = boip.CO408M;
                iCmd.Parameters["@LineNumber"].Value = boip.CO408C;
                iCmd.Parameters["@Vendor"].Value = boip.VEN001H;
                iCmd.Parameters["@PONumber"].Value = boip.CO401E;
                iCmd.Parameters["@POLevel"].Value = boip.CO401F;
                iCmd.Parameters["@CreationDate"].Value = boip.OBF002;
                iCmd.Parameters["@CreationTime"].Value = boip.OBF003;
                iCmd.Parameters["@LastChangeDate"].Value = iMdsNullDate;
                iCmd.Parameters["@LastChangeTime"].Value = iMdsNullTime;
                iCmd.Parameters["@LastChangeUserId"].Value = boip.MBFUID;
                iCmd.Parameters["@LastChangeFunction"].Value = boip.MBFFID;
                iCmd.Parameters["@RecordStatus"].Value = boip.MBFRCS;
                iCmd.ExecuteNonQuery();
            }

            iCmd = new iDB2Command("INSERT INTO " + iseriesLibrary + ".BOCP VALUES(@BookingOrderNo, @GroupNo, @CustomerNo, @BusinessType, @FirstShipDate, @SequenceNo, @CustomerPoNo, @BookingOrderType, @EnumeratedItem, @RepackFactor, @ItemPackNo, @ShipWithOrder, @OriginalOrderQty, @OrderQty, @FromProtected, @ProtectDate, @QtyShipped, @QtyCancelled, @DateCancelled, @PriceOverride, @Salesperson, @DistributionCenter, @Load, @ScheduledDeliveryDate, @Vendor, @Buyer, @PONumber, @POLevel, @QuantityOnOrder, @BookingOrderStatus, @OpenBookingsUpdate, @DateCompleted, @LineNo, @CreationDate, @CreationTime, @LastChangeDate, @LastChangeTime, @LastChangeUserId, @LastChangeFunction, @RecordStatus)", iSeriesConnection);
            iCmd.DeriveParameters();

            var Tbl_bocp = from c in db.Tbl_BOCP
                           select c;


            foreach (var bocp in Tbl_bocp)
            {
                if (bocp.CO99A.Trim() != "")
                {
                    //try
                    //{
                    iCmd.Parameters["@BookingOrderNo"].Value = bocp.CO407F;
                    iCmd.Parameters["@GroupNo"].Value = bocp.CO99A;
                    iCmd.Parameters["@CustomerNo"].Value = bocp.CO300G;
                    iCmd.Parameters["@BusinessType"].Value = bocp.CO100A;
                    iCmd.Parameters["@FirstShipDate"].Value = bocp.CO408N;
                    iCmd.Parameters["@SequenceNo"].Value = bocp.CO403N;
                    iCmd.Parameters["@CustomerPoNo"].Value = bocp.CO408Y;
                    iCmd.Parameters["@BookingOrderType"].Value = bocp.CO408I;
                    iCmd.Parameters["@EnumeratedItem"].Value = bocp.CO1UA;
                    iCmd.Parameters["@RepackFactor"].Value = bocp.CO1ZA;
                    iCmd.Parameters["@ItemPackNo"].Value = bocp.CO1TA;
                    iCmd.Parameters["@ShipWithOrder"].Value = bocp.CO408H;
                    iCmd.Parameters["@OriginalOrderQty"].Value = bocp.CO416P;
                    iCmd.Parameters["@OrderQty"].Value = bocp.CO408Z;
                    iCmd.Parameters["@FromProtected"].Value = bocp.CO408L;
                    iCmd.Parameters["@ProtectDate"].Value = iMdsNullDate;
                    iCmd.Parameters["@QtyShipped"].Value = bocp.CO402Q;
                    iCmd.Parameters["@QtyCancelled"].Value = bocp.CO408P;
                    iCmd.Parameters["@DateCancelled"].Value = iMdsNullDate;
                    iCmd.Parameters["@PriceOverride"].Value = bocp.CO408M;
                    iCmd.Parameters["@SalesPerson"].Value = bocp.CO403R;
                    iCmd.Parameters["@DistributionCenter"].Value = bocp.CO2A;
                    iCmd.Parameters["@Load"].Value = bocp.CO408W;
                    iCmd.Parameters["@ScheduledDeliveryDate"].Value = bocp.CO409V;
                    iCmd.Parameters["@Vendor"].Value = bocp.VEN001H;
                    iCmd.Parameters["@Buyer"].Value = bocp.CO2IA;
                    iCmd.Parameters["@PONumber"].Value = bocp.CO401E;
                    iCmd.Parameters["@POLevel"].Value = bocp.CO401F;
                    iCmd.Parameters["@QuantityOnOrder"].Value = bocp.IM101M;
                    iCmd.Parameters["@BookingOrderStatus"].Value = bocp.CO408F;
                    iCmd.Parameters["@OpenBookingsUpdate"].Value = bocp.BK0001;
                    iCmd.Parameters["@DateCompleted"].Value = iMdsNullDate;
                    iCmd.Parameters["@LineNo"].Value = bocp.CO408C;
                    iCmd.Parameters["@CreationDate"].Value = bocp.OBF002;
                    iCmd.Parameters["@CreationTime"].Value = bocp.OBF003;
                    iCmd.Parameters["@LastChangeDate"].Value = iMdsNullDate;
                    iCmd.Parameters["@LastChangeTime"].Value = iMdsNullTime;
                    iCmd.Parameters["@LastChangeUserId"].Value = bocp.MBFUID;
                    iCmd.Parameters["@LastChangeFunction"].Value = bocp.MBFFID;
                    iCmd.Parameters["@RecordStatus"].Value = bocp.MBFRCS;
                    iCmd.ExecuteNonQuery();
                    //}
                    //catch (Exception)
                    //{

                    //}

                }
            }


            iCmd.Dispose();
            iSeriesConnection.Close();
            iSeriesConnection.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

using SHS.Contracts.SHS2BuiltInModel.Public;
using SHS.Platform.ServiceFx;
using SHS.Platform.ServiceFx.Logging;
using SHS.Sdk.ItineraryManager;
using SHS.Sdk.ItineraryManager.Client;
using SHS.Web.VoiceAgent.Api.Configuration;
using SHS.Web.VoiceAgent.Api.EnterpriseServices.Profiles;
using SHS.Web.VoiceAgent.Api.ErrorHandling;
using SHS.Web.VoiceAgent.Api.Models;
using SHS.Web.VoiceAgent.Api.Models.Profiles;
using SHS.Web.VoiceAgent.Api.Models.Reservation;

using StackExchange.Profiling;

using Synxis.Enterprise.Common;
using Synxis.Enterprise.Logging;

using Reservation = SHS.Web.VoiceAgent.Api.Models.Reservation.Reservation;
using RoomStay = SHS.Web.VoiceAgent.Api.Models.Reservation.RoomStay;
using SHS.Platform.ServiceFx.Metadata.v1;

namespace SHS.Web.VoiceAgent.Api.EnterpriseServices.Reservations
{
    public class ReservationServiceClient : IReservationServiceClient
    {
        #region Static Fields

        private static readonly ILogWrapper Logger = LogWrapperProvider.GetLoggerWrapper(typeof(ReservationServiceClient));

        #endregion

        #region Fields

        private readonly MiniProfiler Profiler = MiniProfiler.Current;

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///     Gets a response for a cancel reservation request by confirmation number from ESS reservation manager service.
        /// </summary>
        /// <param name="cancelReservationRequest">Cancel Reservation Request</param>
        /// <returns>CancelReservationResponse</returns>
        public CancelReservationResponse CancelReservation(CancelReservationRequest cancelReservationRequest)
        {
            using (Profiler.Step("ReservationServiceClient.CancelReservation"))
            {
                var activityId = String.Empty;
                try
                {
                    var header = ContextListBuilder.New().WithBusinessContext(ContextListAppContextSourceBusinessContext.VA)
                        .WithUserId(cancelReservationRequest.UserUniqueId.ToString())
                        .Build();
                    activityId = header.DiagnosticContext.ActivityId;

                    using (var itineraryManagerClient = new ItineraryManagerClient(header, ServiceRegistry.AddressPool.OfType<IItineraryManager>()))
                    {
                        var cancelReservationRQ = new CancelReservationRQ
                                                      {
                                                          Reservation = new CancelReservationRQReservation
                                                                            {
                                                                                CRS_confirmationNumber = cancelReservationRequest.CrsConfirmationNumber
                                                                            },
                                                          UserDetails = new CancelReservationRQUserDetails
                                                                            {
                                                                                Preferences = new CancelReservationRQUserDetailsPreferences
                                                                                                  {
                                                                                                      Language = new Language
                                                                                                                     {
                                                                                                                         Code = WebConstants.DefaultLanguage
                                                                                                                     }
                                                                                                  }
                                                                            }
                                                      };

                        var cancelReservationResponse = itineraryManagerClient.CancelReservation(cancelReservationRQ);

                        var cancelReservationResultResponse = new CancelReservationResponse
                                                                  {
                                                                      ApplicationResults = new ApplicationResultsModel(
                                                                          cancelReservationResponse.ApplicationResults.Success.IsNotNull(),
                                                                          cancelReservationResponse.ApplicationResults.Warning.IfNotNull(
                                                                              w => w.Select(
                                                                                  warning => warning.SystemSpecificResults.ShortText)),
                                                                          cancelReservationResponse.ApplicationResults.Error.IfNotNull(
                                                                              e => e.Select(error => error.SystemSpecificResults.ShortText)
                                                                          )),
                                                                      CrsConfirmationNumber = cancelReservationResponse.Reservation.CRS_confirmationNumber,
                                                                      CancellationNumber = cancelReservationResponse.Reservation.CRS_cancellationNumber
                                                                  };
                        return cancelReservationResultResponse;
                    }
                }
                catch (Exception exception)
                {
                    Logger.AppLogger.Error(
                        "CancelReservationUnhandledException",
                        exception,
                        "UserId".ToKvp(cancelReservationRequest.UserUniqueId),
                        "CrsConfirmationNumber".ToKvp(cancelReservationRequest.CrsConfirmationNumber));
                    throw HttpResponseExceptionHelper.CreateHttpResponseException(activityId, exception);
                }
            }
        }

        /// <summary>
        ///     Creates a reservation in booked status.
        /// </summary>
        /// <param name="reservationRequest">Reservation Request</param>
        /// <returns> Reservation response model</returns>
        public ReservationResponse CreateReservation(ReservationRequest reservationRequest)
        {
            using (Profiler.Step("ReservationServiceClient.CreateReservation"))
            {
                var activityId = String.Empty;
                try
                {
                    var header = ContextListBuilder.New().WithBusinessContext(ContextListAppContextSourceBusinessContext.VA)
                       .WithUserId(reservationRequest.UserUniqueId.ToString())
                       .Build();
                    activityId = header.DiagnosticContext.ActivityId;

                    var createReservationRq = new CreateReservationRQ
                                                  {
                                                      Chain = new CreateReservationRQChain
                                                                  {
                                                                      id = reservationRequest.ChainId
                                                                  },
                                                      Reservation = CreateReservationRequest(reservationRequest),
                                                      ReturnReservationDetails = true,
                                                      UserDetails = new CreateReservationRQUserDetails
                                                                        {
                                                                            Preferences = new CreateReservationRQUserDetailsPreferences
                                                                                              {
                                                                                                  Language = new Language
                                                                                                                 {
                                                                                                                     Code = reservationRequest.Language
                                                                                                                 }
                                                                                              }
                                                                        }
                                                  };

                    using (var itineraryManagerClient = new ItineraryManagerClient(header, ServiceRegistry.AddressPool.OfType<IItineraryManager>()))
                    {
                        var createReservationResponse = itineraryManagerClient.CreateReservation(createReservationRq);

                        var reservationResponse = new ReservationResponse
                                                      {
                                                          ApplicationResults = new ApplicationResultsModel(
                                                              createReservationResponse.ApplicationResults.Success.IfNotNull(result => result.SystemSpecificResults.ShortText.Equals("Success"), false),
                                                              createReservationResponse.ApplicationResults.Warning.IfNotNull(
                                                                  applicationResultWarning => applicationResultWarning.Select(warning => warning.SystemSpecificResults.ShortText),
                                                                  new List<String>()),
                                                              createReservationResponse.ApplicationResults.Error.IfNotNull(applicationResultError => applicationResultError.Select(error => error.SystemSpecificResults.ShortText), new List<String>()))
                                                      };

                        if (reservationResponse.ApplicationResults.Success)
                            reservationResponse.Reservation = MapReservationResponse(createReservationResponse.ReservationList.Reservation);

                        return reservationResponse;
                    }
                }
                catch (Exception exception)
                {
                    Logger.AppLogger.Error(
                        "CreateReservationUnhandledException",
                        exception,
                        "ChainId".ToKvp(reservationRequest.ChainId),
                        "HotelId".ToKvp(reservationRequest.HotelId),
                        "UserId".ToKvp(reservationRequest.UserUniqueId));
                    throw HttpResponseExceptionHelper.CreateHttpResponseException(activityId, exception);
                }
            }
        }

        /// <summary>
        ///     Gets a reservation by confirmation number from ESS reservation manager service.
        /// </summary>
        /// <param name="reservationRequest">Reservation Request</param>
        /// <returns>ReservationSearchResult</returns>
        public ReservationResponse GetReservationSearchResult(ReservationRequest reservationRequest)
        {
            using (Profiler.Step("ReservationServiceClient.GetReservationSearchResult"))
            {
                var activityId = String.Empty;
                try
                {
                    var header = ContextListBuilder.New().WithBusinessContext(ContextListAppContextSourceBusinessContext.VA)
                       .WithUserId(reservationRequest.UserUniqueId.ToString())
                       .Build();
                    activityId = header.DiagnosticContext.ActivityId;

                    if (reservationRequest.SearchItemType.IsNullOrEmpty() ||
                        reservationRequest.SearchItemValue.IsNullOrEmpty())
                        return null;

                    QueryReservationRQQueryType queryType;

                    switch (reservationRequest.SearchItemType)
                    {
                        case "email":
                            queryType = QueryReservationRQQueryType.CRS_ConfirmationNumberAndGuestEmail;
                            break;
                        default:
                            queryType = QueryReservationRQQueryType.CRS_ConfirmationNumber;
                            break;
                    }

                    using (var itineraryManagerClient = new ItineraryManagerClient(header, ServiceRegistry.AddressPool.OfType<IItineraryManager>()))
                    {
                        var queryReservationResponse = itineraryManagerClient.QueryReservation(CreateReservationQueryRequest(queryType, reservationRequest.ChainId, reservationRequest.SearchItemValue));

                        var rez = queryReservationResponse.ReservationList.FirstOrDefault();
                        return new ReservationResponse
                                   {
                                       ApplicationResults = new ApplicationResultsModel(
                                           queryReservationResponse.ApplicationResults.Success.IsNotNull(),
                                           queryReservationResponse.ApplicationResults.Warning.IfNotNull(
                                               w => w.Select(
                                                   warning => warning.SystemSpecificResults.ShortText),
                                               queryReservationResponse.ApplicationResults.Error.IfNotNull(
                                                   e => e.Select(error => error.SystemSpecificResults.ShortText))
                                           )),
                                       Reservation = MapReservationResponse(rez)
                                   };
                    }
                }
                catch (Exception exception)
                {
                    Logger.AppLogger.Error(
                        "GetReservationSearchResultUnhandledException",
                        exception,
                        "SearchItemType".ToKvp(reservationRequest.SearchItemType),
                        "SearchItemValue".ToKvp(reservationRequest.SearchItemValue),
                        "ChainId".ToKvp(reservationRequest.ChainId),
                        "HotelId".ToKvp(reservationRequest.HotelId),
                        "UserId".ToKvp(reservationRequest.UserUniqueId));
                    throw HttpResponseExceptionHelper.CreateHttpResponseException(activityId, exception);
                }
            }
        }

        /// <summary>
        ///     Updates a reservation and sets the status to confirmed.
        /// </summary>
        /// <param name="reservationRequest">Reservation request</param>
        /// <param name="profileServiceClient">Profile service client</param>
        /// <returns></returns>
        public ReservationResponse UpdateReservation(ReservationRequest reservationRequest, IProfileServiceClient profileServiceClient)
        {
            using (Profiler.Step("ReservationServiceClient.UpdateReservation"))
            {
                var activityId = String.Empty;
                try
                {
                    var header = ContextListBuilder.New().WithBusinessContext(ContextListAppContextSourceBusinessContext.VA)
                        .WithUserId(reservationRequest.UserUniqueId.ToString())
                        .Build();
                    activityId = header.DiagnosticContext.ActivityId;

                    var profileRequest = new ProfileRequest
                                             {
                                                 ChainId = reservationRequest.ChainId,
                                                 HotelId = reservationRequest.HotelId,
                                                 TravelerProfileId = reservationRequest.GuestProfileId
                                             };
                    var guestProfile = profileServiceClient.GetTravelerProfile(profileRequest);

                    using (var itineraryManagerClient = new ItineraryManagerClient(header, ServiceRegistry.AddressPool.OfType<IItineraryManager>()))
                    {
                        var updateReservationRq = new UpdateReservationRQ
                                                      {
                                                          Reservation = CreateUpdateReservationRequest(reservationRequest, guestProfile),
                                                          ReturnReservationDetails = true,
                                                          UserDetails = new UpdateReservationRQUserDetails
                                                                            {
                                                                                Preferences = new UpdateReservationRQUserDetailsPreferences
                                                                                                  {
                                                                                                      Language = new Language
                                                                                                                     {
                                                                                                                         Code = reservationRequest.Language
                                                                                                                     }
                                                                                                  }
                                                                            }
                                                      };

                        var updateReservationResponse = itineraryManagerClient.UpdateReservation(updateReservationRq);

                        var reservationResponse = new ReservationResponse
                                                      {
                                                          ApplicationResults = new ApplicationResultsModel(
                                                              updateReservationResponse.ApplicationResults.Success.IfNotNull(result => result.SystemSpecificResults.ShortText.Equals("Success"), false),
                                                              updateReservationResponse.ApplicationResults.Warning.IfNotNull(
                                                                  applicationResultWarning => applicationResultWarning.Select(warning => warning.SystemSpecificResults.ShortText),
                                                                  new List<String>()),
                                                              updateReservationResponse.ApplicationResults.Error.IfNotNull(applicationResultError => applicationResultError.Select(error => error.SystemSpecificResults.ShortText), new List<String>()))
                                                      };

                        if (reservationResponse.ApplicationResults.Success)
                        {
                            reservationResponse.Reservation = MapReservationResponse(updateReservationResponse.ReservationList.Reservation);
                        }

                        return reservationResponse;
                    }
                }
                catch (Exception exception)
                {
                    Logger.AppLogger.Error(
                        "UpdateReservationUnhandledException",
                        exception,
                        "ChainId".ToKvp(reservationRequest.ChainId),
                        "HotelId".ToKvp(reservationRequest.HotelId),
                        "UserId".ToKvp(reservationRequest.UserUniqueId));
                    throw HttpResponseExceptionHelper.CreateHttpResponseException(activityId, exception);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Creates a reservation query request
        /// </summary>
        /// <param name="queryType">Type of query</param>
        /// <param name="chainId">Chain id</param>
        /// <param name="searchItemValue">Value to search</param>
        /// <returns>QueryReservationRQ</returns>
        private static QueryReservationRQ CreateReservationQueryRequest(QueryReservationRQQueryType queryType, Int32 chainId, string searchItemValue)
        {
            var queryReservationRq = new QueryReservationRQ
                                         {
                                             Query = new QueryReservationRQQuery
                                                         {
                                                             type = queryType
                                                         },
                                             Chain = new QueryReservationRQChain
                                                         {
                                                             id = chainId
                                                         },
                                             UserDetails = new QueryReservationRQUserDetails
                                                               {
                                                                   Preferences = new QueryReservationRQUserDetailsPreferences
                                                                                     {
                                                                                         Language = new Language
                                                                                                        {
                                                                                                            Code = WebConstants.DefaultLanguage
                                                                                                        }
                                                                                     }
                                                               }
                                         };

            switch (queryType)
            {
                case QueryReservationRQQueryType.CRS_ConfirmationNumber:
                    queryReservationRq.Query.Reservation = new QueryReservationRQQueryReservation
                                                               {
                                                                   CRS_confirmationNumber = searchItemValue
                                                               };

                    break;
                case QueryReservationRQQueryType.CRS_ConfirmationNumberAndGuestEmail:
                    queryReservationRq.Query.Reservation = new QueryReservationRQQueryReservation
                                                               {
                                                                   GuestList = new QueryReservationRQQueryReservationGuestList
                                                                                   {
                                                                                       Guest = new QueryReservationRQQueryReservationGuestListGuest
                                                                                                   {
                                                                                                       EmailAddress = searchItemValue
                                                                                                   }
                                                                                   }
                                                               };
                    break;
            }

            return queryReservationRq;
        }

        /// <summary>
        ///     Maps a ESS reservation response to a reservation model.
        /// </summary>
        /// <param name="reservation">Contracts.SHS2BuiltInModel.Public.Reservation</param>
        /// <returns>Reservation</returns>
        private static Reservation MapReservationResponse(Contracts.SHS2BuiltInModel.Public.Reservation reservation)
        {
            if (reservation.IsNull())
                return new Reservation();

            var guestList = reservation.GuestList.IfNotNull(
                list => list.Select(
                    guest => new Guest
                    {
                        Prefix = guest.PersonName.Prefix,
                        FirstName = guest.PersonName.GivenName,
                        LastName = guest.PersonName.Surname,
                        MiddleName = guest.PersonName.MiddleName
                    }),
                new List<Guest>());

                   var reservationModel = new Reservation
                                       {
                                           CrsConfirmationNumber = reservation.CRS_confirmationNumber,
                                           Adults = reservation.RoomStay.FirstOrDefault().IfNotNull(roomStay => roomStay.GuestCount.FirstOrDefault().IfNotNull(guestCount => guestCount.NumGuests, 0), 0),
                                           CrsCancelConfirmationNumber = reservation.CRS_cancellationNumber,
                                           CancellationPermitted = reservation.CancellationPermitted,
                                           Children = 0,
                                           BookedBy = "Not Yet Implemented",
                                           BookedOn = reservation.ResActionsActivities.ResActivityList.Where(activity => activity.ActivityType.Equals("BookDate", StringComparison.Ordinal))
                                                                 .Select(activity => activity.Date).FirstOrDefault(),
                                           BookingChannel = reservation.BookingInfo.BookingSource.PrimaryChannel.Description,
                                           Brand = "Not Yet Implemented",
                                           CancelPolicy = new CancelPolicy
                                                              {
                                                                  CancelPenaltyDate = reservation.CancelPolicy.CancelPenaltyDate,
                                                                  ChargeThreshold = reservation.CancelPolicy.ChargeThreshold,
                                                                  ChargeType = Convert.ToInt32(reservation.CancelPolicy.ChargeType),
                                                                  Description = reservation.CancelPolicy.Description,
                                                                  PenaltyAmount = reservation.CancelPolicy.CancelFeeAmount.IsNotNull()
                                                                  ? reservation.CancelPolicy.CancelFeeAmount.Value
                                                                  : (Decimal?)null
                                                              },
                                           RoomStays = reservation.RoomStay
                                                                  .Select(
                                                                      roomStay => new RoomStay
                                                                                      {
                                                                                          CrsConfirmationNumber = roomStay.CRS_confirmationNumber,
                                                                                          RateId = roomStay.Rate.Code,
                                                                                          RoomId = roomStay.Room.Code,
                                                                                          RateName = roomStay.Rate.Name,
                                                                                          RoomName = roomStay.Room.Name,
                                                                                          Nights = roomStay.NumNights,
                                                                                          StartDate = roomStay.StartDate,
                                                                                          EndDate = roomStay.EndDate
                                                                                      }).ToList(),
                                           Currency = reservation.Currency.CurrencyCode,
                                           GuaranteePolicy = new GuaranteePolicy
                                                                 {
                                                                     Description = reservation.GuaranteePolicy.Description
                                                                 },
                                           Guests = guestList,
                                           Hotel = reservation.Hotel.Name,
                                           Status = reservation.Status.ToString(),
                                           RoomRate = new RoomRate
                                                          {
                                                              AveragePrice = new AveragePrice
                                                                                 {
                                                                                     Amount = reservation.RoomPriceList.AveragePricePerNight.Price.TotalAmount,
                                                                                     AmountWithTaxesAndFees = reservation.RoomPriceList.AveragePricePerNight.Price.TotalAmountIncludingTaxesFees,
                                                                                     CurrencyCode = reservation.RoomPriceList.AveragePricePerNight.Price.CurrencyCode
                                                                                 },
                                                              PriceBreakdown = reservation.RoomPriceList.PriceBreakdownList
                                                                                          .Select(
                                                                                              product => new PriceBreakdown
                                                                                                             {
                                                                                                                 CurrencyCode = product.ProductPriceList[0].Price.CurrencyCode,
                                                                                                                 Fees = product.ProductPriceList[0].Price.Fees.Amount,
                                                                                                                 ProductRate = product.ProductPriceList[0].Product.Rate.Code,
                                                                                                                 ProductRoomName = product.ProductPriceList[0].Product.Room.Code,
                                                                                                                 Tax = product.ProductPriceList[0].Price.Tax.Amount,
                                                                                                                 Total = product.ProductPriceList[0].Price.TotalAmount,
                                                                                                                 TotalWithTaxFees = product.ProductPriceList[0].Price.TotalAmountIncludingTaxesFees
                                                                                                             }).ToList(),
                                                              TotalPrice = new TotalPrice
                                                                               {
                                                                                   Fees = new Fees
                                                                                              {
                                                                                                  Amount = reservation.RoomPriceList.TotalPrice.Price.Fees.Amount,
                                                                                                  StayFeeAmount = reservation.RoomPriceList.TotalPrice.Price.Fees.StayFeeAmount,
                                                                                                  Charges = reservation.RoomPriceList.TotalPrice.Price.Fees.BreakDown
                                                                                                                       .Select(
                                                                                                                           charge => new Charge
                                                                                                                                         {
                                                                                                                                             Amount = charge.Amount,
                                                                                                                                             Name = charge.Name,
                                                                                                                                             Type = Convert.ToInt32(charge.Type)
                                                                                                                                         }).ToList()
                                                                                              },
                                                                                   Tax = new Tax
                                                                                             {
                                                                                                 Amount = reservation.RoomPriceList.TotalPrice.Price.Tax.Amount,
                                                                                                 StayTaxAmount = reservation.RoomPriceList.TotalPrice.Price.Tax.StayTaxAmount,
                                                                                                 Charges = reservation.RoomPriceList.TotalPrice.Price.Tax.BreakDown
                                                                                                                      .Select(
                                                                                                                          charge => new Charge
                                                                                                                                        {
                                                                                                                                            Amount = charge.Amount,
                                                                                                                                            Name = charge.Name,
                                                                                                                                            Type = Convert.ToInt32(charge.Type)
                                                                                                                                        }).ToList()
                                                                                             },
                                                                                   Currency = reservation.RoomPriceList.TotalPrice.Price.CurrencyCode,
                                                                                   TotalAmount = reservation.RoomPriceList.TotalPrice.Price.TotalAmount,
                                                                                   TotalWithTaxesFees = reservation.RoomPriceList.TotalPrice.Price.TotalAmountIncludingTaxesFees
                                                                               }
                                                          }
                                       };
            return reservationModel;
        }

        /// <summary>
        ///     Creates a reservation request reservation
        /// </summary>
        /// <param name="reservationRequest">Reservation Request</param>
        /// <returns>CreateReservationRQReservation</returns>
        private CreateReservationRQReservation CreateReservationRequest(ReservationRequest reservationRequest)
        {
            return new CreateReservationRQReservation
                       {
                           Hotel = new CreateReservationRQReservationHotel
                                       {
                                           id = reservationRequest.HotelId
                                       },
                           action = CreateReservationRQReservationAction.Booked,
                           BookingInfo = new CreateReservationRQReservationBookingInfo
                                             {
                                                 BookingSource = new CreateReservationRQReservationBookingInfoBookingSource
                                                                     {
                                                                         IP_Address = reservationRequest.IpAddress,
                                                                         PrimaryChannel = new CreateReservationRQReservationBookingInfoBookingSourcePrimaryChannel
                                                                                              {
                                                                                                  code = WebConstants.PrimaryChannelCode
                                                                                              },
                                                                         SecondaryChannel = new CreateReservationRQReservationBookingInfoBookingSourceSecondaryChannel
                                                                                                {
                                                                                                    code = WebConstants.PrimaryChannelCode
                                                                                                }
                                                                     }
                                             },
                           Comment = reservationRequest.Comment,
                           CouponOfferCode = reservationRequest.CouponOfferCode,
                           Currency = new Currency { CurrencyCode = reservationRequest.CurrencyCode },
                           RoomStay = reservationRequest.GetCreateRoomStay(),
                           NumRooms = reservationRequest.Occupancy.NumberOfRooms
                       };
        }

        /// <summary>
        ///     Creates and update reservation request.
        /// </summary>
        /// <param name="reservationRequest">Reservation request</param>
        /// <param name="profile">Profile</param>
        /// <returns>UpdateReservationRQReservation</returns>
        private UpdateReservationRQReservation CreateUpdateReservationRequest(ReservationRequest reservationRequest, Profile profile)
        {
            return new UpdateReservationRQReservation
                       {
                           CRS_confirmationNumber = reservationRequest.CrsConfirmationNumber,
                           action = UpdateReservationRQReservationAction.Confirmed,
                           BookingInfo = new UpdateReservationRQReservationBookingInfo
                                             {
                                                 BookingSource = new UpdateReservationRQReservationBookingInfoBookingSource
                                                                     {
                                                                         IP_Address = reservationRequest.IpAddress,
                                                                         PrimaryChannel = new UpdateReservationRQReservationBookingInfoBookingSourcePrimaryChannel
                                                                                              {
                                                                                                  code = WebConstants.PrimaryChannelCode
                                                                                              },
                                                                         SecondaryChannel = new UpdateReservationRQReservationBookingInfoBookingSourceSecondaryChannel
                                                                                                {
                                                                                                    code = WebConstants.PrimaryChannelCode
                                                                                                }
                                                                     }
                                             },
                           //Comment = reservationRequest.Comment,
                           //CouponOfferCode = reservationRequest.CouponOfferCode,
                           Currency = new Currency { CurrencyCode = reservationRequest.CurrencyCode },
                           RoomStay = reservationRequest.GetUpdateRoomStay(),
                           NumRooms = reservationRequest.Occupancy.NumberOfRooms,
                           Guest = reservationRequest.GetGuest(profile).ToArray()
                       };
        }

        #endregion
    }
}
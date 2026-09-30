using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HotelBooking.Core;
using Xunit;
using Moq;

namespace HotelBooking.UnitTests{
    public class BookingManagerTests{
        private IBookingManager bookingManager;
        private Mock<IRepository<Booking>> mockBookingRepository;
        private Mock<IRepository<Room>> mockRoomRepository;

        public BookingManagerTests(){
            mockBookingRepository = new Mock<IRepository<Booking>>();
            mockRoomRepository = new Mock<IRepository<Room>>();

            //  fake rooms
            var rooms = new List<Room>{
                new Room { Id = 1, Description = "Room 1" },
                new Room { Id = 2, Description = "Room 2" },
                new Room { Id = 3, Description = "Room 3" },
                new Room { Id = 4, Description = "Room 4" }
            };

        
            var bookings = new List<Booking>{
                // Room 1 is booked for 3 nights
                new Booking { Id = 1, RoomId = 1, StartDate = DateTime.Today.AddDays(1), EndDate = DateTime.Today.AddDays(4), IsActive = true },
                // room 3 is booked for a whole week
                new Booking { Id = 2, RoomId = 3, StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(17), IsActive = true },
                // room 4 has a CANCELLED (inactive) booking during Room 1's time
                new Booking { Id = 3, RoomId = 4, StartDate = DateTime.Today.AddDays(1), EndDate = DateTime.Today.AddDays(4), IsActive = false }
            };
            
            mockRoomRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(rooms);
            mockBookingRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(bookings);

            bookingManager = new BookingManager(mockBookingRepository.Object, mockRoomRepository.Object);
        }

        [Theory]
        [InlineData(0, 0)]   
        [InlineData(-1, 5)]  
        [InlineData(5, 3)]   
        public async Task FindAvailableRoom_InvalidDates_ThrowsArgumentException(int startOffsetDays, int endOffsetDays){
            // Arrange
            DateTime startDate = DateTime.Today.AddDays(startOffsetDays);
            DateTime endDate = DateTime.Today.AddDays(endOffsetDays);

            // Act
            Func<Task> act = async () => await bookingManager.FindAvailableRoom(startDate, endDate);

            // Assert
            await Assert.ThrowsAsync<ArgumentException>(act);
        }

        [Theory]
        [InlineData(1, 4)]   
        [InlineData(10, 17)] 
        [InlineData(20, 25)] 
        public async Task FindAvailableRoom_RoomAvailable_ReturnsValidRoomId(int startOffsetDays, int endOffsetDays){
            DateTime startDate = DateTime.Today.AddDays(startOffsetDays);
            DateTime endDate = DateTime.Today.AddDays(endOffsetDays);

            int roomId = await bookingManager.FindAvailableRoom(startDate, endDate);

            Assert.True(roomId > 0); 
        }

        [Fact]
        public async Task CreateBooking_RoomAvailable_CallsAddAsyncAndReturnsTrue(){
            var newBooking = new Booking 
            { 
                StartDate = DateTime.Today.AddDays(20), 
                EndDate = DateTime.Today.AddDays(25) 
            };

            bool result = await bookingManager.CreateBooking(newBooking);

            Assert.True(result);
            Assert.True(newBooking.IsActive);
            
            mockBookingRepository.Verify(repo => repo.AddAsync(It.IsAny<Booking>()), Times.Once);
        }

        [Fact]
        public async Task CreateBooking_NoRoomsAvailable_ReturnsFalseAndDoesNotCallAddAsync(){
            var rooms = new List<Room> { 
                new Room { Id = 1 }, 
                new Room { Id = 2 } 
            };

            var fullyBookedDates = new List<Booking>{
                new Booking { RoomId = 1, StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(15), IsActive = true },
                new Booking { RoomId = 2, StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(15), IsActive = true }
            };

            mockRoomRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(rooms);
            mockBookingRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(fullyBookedDates);

            var newBooking = new Booking { StartDate = DateTime.Today.AddDays(11), EndDate = DateTime.Today.AddDays(13) };

            bool result = await bookingManager.CreateBooking(newBooking);

            Assert.False(result);

            mockBookingRepository.Verify(repo => repo.AddAsync(It.IsAny<Booking>()), Times.Never);
        }

        [Fact]
        public async Task CreateBooking_RoomHasCancelledBooking_SuccessfullyBooksAndCallsAddAsync(){
            var rooms = new List<Room> { new Room { Id = 1 } };
            var cancelledBookings = new List<Booking>{
                new Booking { RoomId = 1, StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(15), IsActive = false } 
            };

            mockRoomRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(rooms);
            mockBookingRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(cancelledBookings);

            var newBooking = new Booking { StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(15) };

            bool result = await bookingManager.CreateBooking(newBooking);

            Assert.True(result); 
            Assert.Equal(1, newBooking.RoomId); 
            Assert.True(newBooking.IsActive); 

            mockBookingRepository.Verify(repo => repo.AddAsync(It.IsAny<Booking>()), Times.Once);
        }

        [Fact]
        public async Task CreateBooking_RoomAvailable_SetsPropertiesAndCallsAddAsync(){
            var rooms = new List<Room> { new Room { Id = 1 } };
            mockRoomRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(rooms);
            mockBookingRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(new List<Booking>());

            var newBooking = new Booking { StartDate = DateTime.Today.AddDays(1), EndDate = DateTime.Today.AddDays(5) };

            bool result = await bookingManager.CreateBooking(newBooking);

            Assert.True(result); 
            Assert.Equal(1, newBooking.RoomId);
            Assert.True(newBooking.IsActive);
            mockBookingRepository.Verify(repo => repo.AddAsync(newBooking), Times.Once);
        }

        [Theory]
        [InlineData(1, 5, 0)]  
        [InlineData(10, 11, 0)] 
        [InlineData(12, 15, 4)] 
        [InlineData(10, 15, 4)] 
        [InlineData(16, 18, 0)] 
        public async Task GetFullyOccupiedDates_VariousPeriods_ReturnsCorrectNumberOfDates(
            int startOffsetDays, int endOffsetDays, int expectedOccupiedDaysCount){
            var rooms = new List<Room> { new Room { Id = 1 }, new Room { Id = 2 } };
            
            var bookings = new List<Booking>{
                new Booking { RoomId = 1, StartDate = DateTime.Today.AddDays(10), EndDate = DateTime.Today.AddDays(15), IsActive = true },
                
                new Booking { RoomId = 2, StartDate = DateTime.Today.AddDays(12), EndDate = DateTime.Today.AddDays(15), IsActive = true },
                
                new Booking { RoomId = 2, StartDate = DateTime.Today.AddDays(16), EndDate = DateTime.Today.AddDays(18), IsActive = false }
            };

            mockRoomRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(rooms);
            mockBookingRepository.Setup(repo => repo.GetAllAsync()).ReturnsAsync(bookings);

            DateTime startDate = DateTime.Today.AddDays(startOffsetDays);
            DateTime endDate = DateTime.Today.AddDays(endOffsetDays);

            var fullyOccupiedDates = await bookingManager.GetFullyOccupiedDates(startDate, endDate);

            Assert.Equal(expectedOccupiedDaysCount, fullyOccupiedDates.Count);
        }

        [Fact]
        public async Task GetFullyOccupiedDates_StartDateAfterEndDate_ThrowsArgumentException(){
            DateTime startDate = DateTime.Today.AddDays(5);
            DateTime endDate = DateTime.Today.AddDays(1);

            Func<Task> act = async () => await bookingManager.GetFullyOccupiedDates(startDate, endDate);

            await Assert.ThrowsAsync<ArgumentException>(act);
        }
    }

    
}
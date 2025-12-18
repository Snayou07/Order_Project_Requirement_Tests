using Moq;
using Xunit;
using Order_Project.Models;
using Order_Project.Services;
using Order_Project.Services.Intefraces;
using System;
using System.Collections.Generic;

namespace Order_Project_Tests
{
    public class OrderServiceTests
    {
        // Моки залежностей
        private readonly Mock<IInventoryService> _invMock;
        private readonly Mock<IPaymentService> _payMock;
        private readonly Mock<INotificationService> _noteMock;
        private readonly Mock<IDiscountService> _discMock;

        // Тестований сервіс
        private readonly OrderService _service;

        public OrderServiceTests()
        {
            _invMock = new Mock<IInventoryService>();
            _payMock = new Mock<IPaymentService>();
            _noteMock = new Mock<INotificationService>();
            _discMock = new Mock<IDiscountService>();

            _service = new OrderService(
                _invMock.Object,
                _payMock.Object,
                _noteMock.Object,
                _discMock.Object
            );
        }

        // R1. Назва продукту повинна бути валідною (>= 3 символи).
        [Fact]
        public void Requirement01_ShouldThrowException_WhenProductNameIsTooShort()
        {
            // Arrange
            string shortName = "TV";

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateOrder(shortName, 1, 100));

            Assert.Contains("too short", ex.Message);
        }

        // R2. Кількість товару повинна бути додатною.
        [Fact]
        public void Requirement02_ShouldThrowException_WhenQuantityIsZeroOrNegative()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateOrder("Laptop", 0, 100));

            Assert.Contains("positive", ex.Message);
        }

        // R3. Максимальна кількість товару – 100 одиниць.
        [Fact]
        public void Requirement03_ShouldThrowException_WhenQuantityExceedsLimit()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateOrder("Laptop", 101, 100));

            Assert.Contains("exceeds max limit", ex.Message);
        }

        // R4. Пріоритет може бути лише "Low", "Normal", "High".
        [Fact]
        public void Requirement04_ShouldThrowException_WhenPriorityIsInvalid()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.CreateOrder("Laptop", 1, 100, priority: "Critical"));

            Assert.Contains("Invalid priority", ex.Message);
        }

        // R5. Знижка не може перевищувати 30% від суми.
        [Fact]
        public void Requirement05_ShouldThrowException_WhenDiscountIsTooLarge()
        {
            // Arrange
            decimal unitPrice = 100;
            int quantity = 1; // Subtotal = 100

            // Знижка 35 > 30% від 100
            _discMock.Setup(d => d.ValidateCode("BIGSALE")).Returns(35);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.CreateOrder("Laptop", quantity, unitPrice, "Normal", "BIGSALE"));

            Assert.Equal("Discount too large.", ex.Message);
        }

        // R6. До фінальної ціни додається податок 20%.
        [Fact]
        public void Requirement06_ShouldApply20PercentTax_ToFinalPrice()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            // Act
            var order = _service.CreateOrder("Phone", 1, 100);
            // Subtotal=100. Tax=20%. Total має бути 120.

            // Assert
            Assert.Equal(120, order.TotalPrice);
        }

        // R7. Для пріоритету "High" товар резервується.
        [Fact]
        public void Requirement07_ShouldReserveStock_WhenPriorityIsHigh()
        {
            // Arrange
            _invMock.Setup(i => i.ReserveStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            // Act
            _service.CreateOrder("Phone", 1, 100, priority: "High");

            // Assert
            _invMock.Verify(i => i.ReserveStock("Phone", 1), Times.Once);
            _invMock.Verify(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        // R8. Для звичайних пріоритетів наявність перевіряється (CheckStock).
        [Fact]
        public void Requirement08_ShouldCheckStock_WhenPriorityIsNormal()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            // Act
            _service.CreateOrder("Phone", 1, 100, priority: "Normal");

            // Assert
            _invMock.Verify(i => i.CheckStock("Phone", 1), Times.Once);
        }

        // R9. Якщо потрібне ручне схвалення, статус стає PendingApproval.
        [Fact]
        public void Requirement09_ShouldSetStatusPendingApproval_WhenManualCheckNeeded()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.NeedsManualApproval(It.IsAny<Order>())).Returns(true);

            // Act
            var order = _service.CreateOrder("Phone", 1, 100);

            // Assert
            Assert.Equal("PendingApproval", order.State);
            _noteMock.Verify(n => n.SendPendingApproval(order), Times.Once);
        }

        // R10. Успішна оплата встановлює статус Paid.
        [Fact]
        public void Requirement10_ShouldSetStatusPaid_WhenPaymentSuccess()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            // Act
            var order = _service.CreateOrder("Phone", 1, 100);

            // Assert
            Assert.Equal("Paid", order.State);
            _noteMock.Verify(n => n.SendPaidConfirmation(order), Times.Once);
        }

        // R11. При збої оплати для High пріоритету резерв знімається.
        [Fact]
        public void Requirement11_ShouldReleaseStock_WhenPaymentFailsForHighPriority()
        {
            // Arrange
            _invMock.Setup(i => i.ReserveStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(false); // Fail

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _service.CreateOrder("Phone", 1, 100, priority: "High"));

            Assert.Equal("Payment failed.", ex.Message);
            _invMock.Verify(i => i.ReleaseReservedStock("Phone", 1), Times.Once);
        }

        // R12. Не можна оновлювати замовлення, створені понад 30 днів тому.
        [Fact]
        public void Requirement12_ShouldNotUpdate_WhenOrderIsTooOld()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var order = _service.CreateOrder("Phone", 1, 100);

            // "Старимо" замовлення вручну для тесту
            order.CreatedAt = DateTime.UtcNow.AddDays(-31);

            // Act
            bool result = _service.UpdateOrder(order.Id, 5);

            // Assert
            Assert.False(result);
        }

        // R13. Неможливо скасувати замовлення зі статусом "Shipped".
        [Fact]
        public void Requirement13_ShouldNotCancel_WhenStatusIsShipped()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var order = _service.CreateOrder("Phone", 1, 100);
            order.State = "Shipped"; // Імітація відправки

            // Act
            bool result = _service.CancelOrder(order.Id);

            // Assert
            Assert.False(result);
        }

        // R14. При скасуванні замовлення воно додається в AuditLog.
        [Fact]
        public void Requirement14_ShouldAddToAuditLog_WhenCancelled()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            var order = _service.CreateOrder("Phone", 1, 100);

            // Act
            bool result = _service.CancelOrder(order.Id);

            // Assert
            Assert.True(result);
            Assert.Equal("Cancelled", order.State);
            Assert.Contains(order, _service.GetAuditLog());
        }

        // R15. При успішному створенні замовлення йому присвоюється унікальний ID.
        [Fact]
        public void Requirement15_ShouldAssignUniqueIds_Sequentially()
        {
            // Arrange
            _invMock.Setup(i => i.CheckStock(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
            _payMock.Setup(p => p.ProcessPayment(It.IsAny<Order>())).Returns(true);

            // Act
            var order1 = _service.CreateOrder("ItemA", 1, 10);
            var order2 = _service.CreateOrder("ItemB", 1, 10);

            // Assert
            Assert.Equal(1, order1.Id);
            Assert.Equal(2, order2.Id);
        }
    }
}

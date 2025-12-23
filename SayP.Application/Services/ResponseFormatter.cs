using Newtonsoft.Json.Linq;
using SayP.Domain.Enums;

namespace SayP.Application.Services;

/// <summary>
/// Formats command execution results into user-friendly WhatsApp messages
/// Supports Turkish and English
/// </summary>
public class ResponseFormatter
{
    private static string _lastUserMessage = string.Empty;
    
    /// <summary>
    /// Set the last user message to detect language
    /// </summary>
    public static void SetUserMessage(string message)
    {
        _lastUserMessage = message;
    }
    
    /// <summary>
    /// Detect if user is using English (simple heuristic)
    /// </summary>
    private static bool IsEnglish()
    {
        if (string.IsNullOrEmpty(_lastUserMessage)) return false;
        
        var englishKeywords = new[] { "list", "show", "create", "add", "update", "delete", "get", "appointment", "customer", "product" };
        var turkishKeywords = new[] { "listele", "göster", "oluştur", "ekle", "güncelle", "sil", "randevu", "müşteri", "ürün" };
        
        var lowerMessage = _lastUserMessage.ToLowerInvariant();
        var englishCount = englishKeywords.Count(k => lowerMessage.Contains(k));
        var turkishCount = turkishKeywords.Count(k => lowerMessage.Contains(k));
        
        return englishCount > turkishCount;
    }
    /// <summary>
    /// Format success response with detailed information
    /// </summary>
    public static string FormatSuccessResponse(CommandType commandType, string? resultJson)
    {
        if (string.IsNullOrEmpty(resultJson))
        {
            return IsEnglish() ? "✅ Operation completed successfully!" : "✅ İşlem başarıyla tamamlandı!";
        }

        try
        {
            // DEBUG: Log the raw JSON to see what backend returned
            Console.WriteLine($"[DEBUG] ResponseFormatter - CommandType: {commandType}");
            Console.WriteLine($"[DEBUG] ResponseFormatter - Raw JSON (first 500 chars): {(resultJson?.Length > 500 ? resultJson.Substring(0, 500) : resultJson)}");
            
            // Check for null or empty JSON
            if (string.IsNullOrEmpty(resultJson))
            {
                return IsEnglish() ? "✅ Operation completed successfully!" : "✅ İşlem başarıyla tamamlandı!";
            }
            
            // Try to parse as JToken first (handles both objects and arrays)
            var token = JToken.Parse(resultJson);
            
            Console.WriteLine($"[DEBUG] ResponseFormatter - Token Type: {token.Type}");
            
            // If it's an array, wrap it in an object for consistent handling
            JObject result;
            if (token is JArray array)
            {
                Console.WriteLine($"[DEBUG] ResponseFormatter - Detected array with {array.Count} items");
                result = new JObject { ["value"] = array };
            }
            else
            {
                result = (JObject)token;
            }

            return commandType switch
            {
                CommandType.CreateProduct => FormatProductCreated(result),
                CommandType.UpdateProduct => FormatProductUpdated(result),
                CommandType.GetProduct => FormatProductDetails(result),
                CommandType.GetProductByCode => FormatProductDetails(result),
                CommandType.GetProductsByCodes => FormatProductList(result),
                CommandType.GetTodaysProducts => FormatTodaysProducts(result),
                CommandType.ListProducts => FormatProductList(result),
                CommandType.CreateCustomer => FormatCustomerCreated(result),
                CommandType.ListCustomers => FormatCustomerList(result),
                CommandType.CreateInvoice => FormatInvoiceCreated(result),
                CommandType.CreateContract => FormatContractCreated(result),
                CommandType.CreateAppointment => FormatAppointmentCreated(result),
                CommandType.ListAppointments => FormatAppointmentList(result),
                CommandType.ListTodayAppointments => FormatAppointmentList(result),
                CommandType.CheckAvailability => FormatAvailabilityCheck(result),
                _ => IsEnglish() ? "✅ Operation completed successfully!" : "✅ İşlem başarıyla tamamlandı!"
            };
        }
        catch
        {
            return IsEnglish() ? "✅ Operation completed successfully!" : "✅ İşlem başarıyla tamamlandı!";
        }
    }

    private static string FormatProductCreated(JObject result)
    {
        var name = result["name"]?.ToString() ?? (IsEnglish() ? "Product" : "Ürün");
        var price = result["price"]?.ToString() ?? "0";
        var unit = result["unit"]?.ToString() ?? (IsEnglish() ? "Unit" : "Adet");
        var taxRate = result["taxRate"]?.ToString() ?? "0";
        var code = result["code"]?.ToString() ?? "";
        var id = result["id"]?.ToString() ?? "";

        if (IsEnglish())
        {
            return $@"✅ *Product Created Successfully!*

📦 *{name}*
🏷️ *Product Code: {code}*
💰 Price: {price} TL
📊 Unit: {unit}
🧾 Tax: %{taxRate}
🔖 ID: {id}

Your product has been saved and is ready to use!
💡 *Tip:* You can now manage this product by its code: `{code}`";
        }
        
        return $@"✅ *Ürün Başarıyla Oluşturuldu!*

📦 *{name}*
🏷️ *Ürün Kodu: {code}*
💰 Fiyat: {price} TL
📊 Birim: {unit}
🧾 KDV: %{taxRate}
🔖 ID: {id}

Ürününüz sisteme kaydedildi ve kullanıma hazır!
💡 *İpucu:* Artık bu ürünü kodu ile de yönetebilirsiniz: `{code}`";
    }

    private static string FormatProductUpdated(JObject result)
    {
        var name = result["name"]?.ToString() ?? (IsEnglish() ? "Product" : "Ürün");
        return IsEnglish() 
            ? $"✅ *{name}* updated successfully!"
            : $"✅ *{name}* başarıyla güncellendi!";
    }

    private static string FormatCustomerCreated(JObject result)
    {
        var name = result["name"]?.ToString() ?? (IsEnglish() ? "Customer" : "Müşteri");
        var email = result["email"]?.ToString();
        var phone = result["phone"]?.ToString();

        var message = IsEnglish()
            ? $@"✅ *Customer Created Successfully!*

👤 *{name}*"
            : $@"✅ *Müşteri Başarıyla Oluşturuldu!*

👤 *{name}*";

        if (!string.IsNullOrEmpty(email))
            message += $"\n📧 {email}";
        
        if (!string.IsNullOrEmpty(phone))
            message += $"\n📱 {phone}";

        return message + (IsEnglish() 
            ? "\n\nCustomer has been saved to the system!"
            : "\n\nMüşteri sisteme kaydedildi!");
    }

    private static string FormatCustomerList(JObject result)
    {
        // Try to get customers array
        JArray? customers = null;
        
        if (result["items"] is JArray itemsArray)
        {
            customers = itemsArray;
        }
        else if (result["customers"] is JArray customersArray)
        {
            customers = customersArray;
        }
        else if (result["value"] is JArray valueArray)
        {
            customers = valueArray;
        }
        else if (result["data"] is JArray dataArray)
        {
            customers = dataArray;
        }
        
        if (customers == null || customers.Count == 0)
        {
            return IsEnglish() 
                ? "👥 No customers found yet."
                : "👥 Henüz müşteri bulunmuyor.";
        }

        var totalCount = result["totalCount"]?.ToObject<int>() ?? customers.Count;
        var message = IsEnglish()
            ? $"👥 *Customer List* ({totalCount} customers)\n\n"
            : $"👥 *Müşteri Listesi* ({totalCount} müşteri)\n\n";

        foreach (var customer in customers.Take(10))
        {
            var name = customer["name"]?.ToString() ?? "İsimsiz";
            var phone = customer["phone"]?.ToString();
            var email = customer["email"]?.ToString();
            var taxNo = customer["taxNo"]?.ToString();
            
            message += $"👤 *{name}*\n";
            
            if (!string.IsNullOrEmpty(phone))
                message += $"📱 {phone}\n";
            
            if (!string.IsNullOrEmpty(email))
                message += $"📧 {email}\n";
            
            if (!string.IsNullOrEmpty(taxNo))
                message += $"🏢 VKN: {taxNo}\n";
            
            message += "\n";
        }

        if (totalCount > 10)
        {
            message += IsEnglish()
                ? $"... and {totalCount - 10} more customers"
                : $"... ve {totalCount - 10} müşteri daha";
        }

        return message;
    }

    private static string FormatInvoiceCreated(JObject result)
    {
        var invoiceNumber = result["invoiceNumber"]?.ToString() ?? "";
        var total = result["total"]?.ToString() ?? "0";
        var customerName = result["customerName"]?.ToString() ?? (IsEnglish() ? "Customer" : "Müşteri");

        if (IsEnglish())
        {
            return $@"✅ *Invoice Created Successfully!*

🧾 Invoice No: {invoiceNumber}
👤 Customer: {customerName}
💰 Total: {total} TL

Invoice is ready and can be sent!";
        }

        return $@"✅ *Fatura Başarıyla Oluşturuldu!*

🧾 Fatura No: {invoiceNumber}
👤 Müşteri: {customerName}
💰 Toplam: {total} TL

Fatura hazır ve gönderilebilir!";
    }

    private static string FormatContractCreated(JObject result)
    {
        var contractNumber = result["contractNumber"]?.ToString() ?? "";
        var customerName = result["customerName"]?.ToString() ?? (IsEnglish() ? "Customer" : "Müşteri");

        if (IsEnglish())
        {
            return $@"✅ *Contract Created Successfully!*

📄 Contract No: {contractNumber}
👤 Customer: {customerName}

Contract is ready!";
        }

        return $@"✅ *Sözleşme Başarıyla Oluşturuldu!*

📄 Sözleşme No: {contractNumber}
👤 Müşteri: {customerName}

Sözleşme hazır!";
    }

    private static string FormatProductList(JObject result)
    {
        // Try to get products array - backend might return direct array or wrapped in "products" field
        JArray? products = null;
        
        // Check if result has "items" field (pagination response)
        if (result["items"] is JArray itemsArray)
        {
            products = itemsArray;
        }
        // Check if result has "products" field
        else if (result["products"] is JArray productsArray)
        {
            products = productsArray;
        }
        // Check if result has "value" field (OData style or wrapped array)
        else if (result["value"] is JArray valueArray)
        {
            products = valueArray;
        }
        // Check if result has "data" field
        else if (result["data"] is JArray dataArray)
        {
            products = dataArray;
        }
        
        if (products == null || products.Count == 0)
        {
            return IsEnglish()
                ? "📦 No products found yet."
                : "📦 Henüz ürün bulunmuyor.";
        }

        var message = IsEnglish()
            ? $"📦 *Product List* ({products.Count} items)\n\n"
            : $"📦 *Ürün Listesi* ({products.Count} adet)\n\n";

        foreach (var product in products.Take(10))
        {
            var name = product["name"]?.ToString() ?? "Ürün";
            var code = product["code"]?.ToString() ?? "";
            var price = product["price"]?.ToString() ?? "0";
            var stock = product["stockQuantity"]?.ToString() ?? "0";
            
            message += $"📦 *{name}*\n";
            message += $"🏷️ {code}\n";
            message += $"💰 {price} TL\n";
            message += $"📊 Stok: {stock}\n\n";
        }

        if (products.Count > 10)
        {
            message += IsEnglish()
                ? $"... and {products.Count - 10} more products"
                : $"... ve {products.Count - 10} ürün daha";
        }

        return message;
    }

    private static string FormatProductDetails(JObject result)
    {
        var name = result["name"]?.ToString() ?? (IsEnglish() ? "Product" : "Ürün");
        var code = result["code"]?.ToString() ?? "";
        var price = result["price"]?.ToString() ?? "0";
        var unit = result["unit"]?.ToString() ?? (IsEnglish() ? "Unit" : "Adet");
        var taxRate = result["taxRate"]?.ToString() ?? "0";
        var stockQuantity = result["stockQuantity"]?.ToString() ?? "0";
        var description = result["description"]?.ToString() ?? "";
        var isActive = result["isActive"]?.ToObject<bool>() ?? true;

        var status = isActive 
            ? (IsEnglish() ? "🟢 Active" : "🟢 Aktif")
            : (IsEnglish() ? "🔴 Inactive" : "🔴 Pasif");

        if (IsEnglish())
        {
            return $@"📦 *Product Details*

*{name}*
🏷️ Code: `{code}`
💰 Price: {price} TL
📊 Unit: {unit}
🧾 Tax: %{taxRate}
📦 Stock: {stockQuantity}
{status}

{(string.IsNullOrEmpty(description) ? "" : $"📝 Description: {description}")}";
        }

        return $@"📦 *Ürün Detayları*

*{name}*
🏷️ Kod: `{code}`
💰 Fiyat: {price} TL
📊 Birim: {unit}
🧾 KDV: %{taxRate}
📦 Stok: {stockQuantity}
{status}

{(string.IsNullOrEmpty(description) ? "" : $"📝 Açıklama: {description}")}";
    }

    private static string FormatTodaysProducts(JObject result)
    {
        if (result["value"] is JArray products && products.Count > 0)
        {
            var message = IsEnglish()
                ? $"📅 *Products Created Today* ({products.Count} items)\n\n"
                : $"📅 *Bugün Oluşturulan Ürünler* ({products.Count} adet)\n\n";

            foreach (var product in products.Take(5))
            {
                var name = product["name"]?.ToString() ?? (IsEnglish() ? "Product" : "Ürün");
                var code = product["code"]?.ToString() ?? "";
                var price = product["price"]?.ToString() ?? "0";
                
                message += $"📦 *{name}*\n";
                message += $"🏷️ {code} - {price} TL\n\n";
            }

            if (products.Count > 5)
            {
                message += IsEnglish()
                    ? $"... and {products.Count - 5} more products"
                    : $"... ve {products.Count - 5} ürün daha";
            }

            return message;
        }

        return IsEnglish()
            ? "📅 *No products created today yet.*\n\n💡 To create a new product: \"Create laptop product 15000 TL\""
            : "📅 *Bugün henüz ürün oluşturulmamış.*\n\n💡 Yeni ürün oluşturmak için: \"Laptop ürünü oluştur 15000 TL\"";
    }

    private static string FormatAppointmentCreated(JObject result)
    {
        var customerName = result["customerName"]?.ToString() ?? (IsEnglish() ? "Customer" : "Müşteri");
        var productName = result["productName"]?.ToString() ?? (IsEnglish() ? "Service" : "Hizmet");
        var appointmentDate = result["appointmentDate"]?.ToString();
        var startTime = result["startTime"]?.ToString();
        var endTime = result["endTime"]?.ToString();
        var price = result["price"]?.ToString();

        // Format date
        string formattedDate;
        if (!string.IsNullOrEmpty(appointmentDate) && DateTime.TryParse(appointmentDate, out DateTime date))
        {
            formattedDate = IsEnglish()
                ? date.ToString("MMMM dd, yyyy dddd", new System.Globalization.CultureInfo("en-US"))
                : date.ToString("dd MMMM yyyy dddd", new System.Globalization.CultureInfo("tr-TR"));
        }
        else
        {
            formattedDate = IsEnglish() ? "Date not specified" : "Tarih belirtilmedi";
        }

        // Format time
        string formattedTime = "";
        if (!string.IsNullOrEmpty(startTime))
        {
            if (TimeSpan.TryParse(startTime, out TimeSpan start))
            {
                formattedTime = start.ToString(@"hh\:mm");
                if (!string.IsNullOrEmpty(endTime) && TimeSpan.TryParse(endTime, out TimeSpan end))
                {
                    formattedTime += $" - {end.ToString(@"hh\:mm")}";
                }
            }
        }

        var message = IsEnglish()
            ? $@"✅ *Appointment Created Successfully!*

👤 *Customer:* {customerName}
💈 *Service:* {productName}
📅 *Date:* {formattedDate}
🕐 *Time:* {formattedTime}"
            : $@"✅ *Randevu Başarıyla Oluşturuldu!*

👤 *Müşteri:* {customerName}
💈 *Hizmet:* {productName}
📅 *Tarih:* {formattedDate}
🕐 *Saat:* {formattedTime}";

        if (!string.IsNullOrEmpty(price))
        {
            message += IsEnglish()
                ? $"\n💰 *Price:* {price} TL"
                : $"\n💰 *Ücret:* {price} TL";
        }

        message += IsEnglish()
            ? "\n\n✨ Appointment has been saved to the system!"
            : "\n\n✨ Randevu sisteme kaydedildi!";

        return message;
    }

    private static string FormatAppointmentList(JObject result)
    {
        // Try to get appointments array
        JArray? appointments = null;
        
        if (result["items"] is JArray itemsArray)
        {
            appointments = itemsArray;
        }
        else if (result["appointments"] is JArray appointmentsArray)
        {
            appointments = appointmentsArray;
        }
        else if (result["value"] is JArray valueArray)
        {
            appointments = valueArray;
        }
        else if (result["data"] is JArray dataArray)
        {
            appointments = dataArray;
        }
        
        if (appointments == null || appointments.Count == 0)
        {
            return IsEnglish()
                ? "📅 No appointments found for this date."
                : "📅 Bu tarih için randevu bulunmuyor.";
        }

        var totalCount = result["totalCount"]?.ToObject<int>() ?? appointments.Count;
        var message = IsEnglish()
            ? $"📅 *Appointment List* ({totalCount} appointments)\n\n"
            : $"📅 *Randevu Listesi* ({totalCount} randevu)\n\n";

        foreach (var appointment in appointments.Take(10))
        {
            var customerName = appointment["customerName"]?.ToString() ?? "Müşteri";
            var productName = appointment["productName"]?.ToString() ?? "Hizmet";
            var appointmentDate = appointment["appointmentDate"]?.ToString();
            var startTime = appointment["startTime"]?.ToString();
            var status = appointment["status"]?.ToString();

            // Format date
            string formattedDate = "";
            if (!string.IsNullOrEmpty(appointmentDate) && DateTime.TryParse(appointmentDate, out DateTime date))
            {
                formattedDate = date.ToString("dd MMM", new System.Globalization.CultureInfo("tr-TR"));
            }

            // Format time
            string formattedTime = "";
            if (!string.IsNullOrEmpty(startTime) && TimeSpan.TryParse(startTime, out TimeSpan start))
            {
                formattedTime = start.ToString(@"hh\:mm");
            }

            // Status emoji
            string statusEmoji = status switch
            {
                "1" => "✅", // Confirmed
                "2" => "⏳", // Pending
                "3" => "❌", // Cancelled
                _ => "📌"
            };

            message += $"{statusEmoji} *{customerName}* - {productName}\n";
            message += $"   📅 {formattedDate} 🕐 {formattedTime}\n\n";
        }

        if (totalCount > 10)
        {
            message += $"... ve {totalCount - 10} randevu daha";
        }

        return message;
    }

    private static string FormatAvailabilityCheck(JObject result)
    {
        var isAvailable = result["isAvailable"]?.ToObject<bool>() ?? false;
        var date = result["date"]?.ToString();
        var time = result["time"]?.ToString();

        if (isAvailable)
        {
            return $"✅ *Müsait!*\n\n📅 {date}\n🕐 {time}\n\nBu saat için randevu oluşturabilirsiniz.";
        }
        else
        {
            var message = $"❌ *Dolu!*\n\n📅 {date}\n🕐 {time}\n\nBu saat dolu.";
            
            // Check for alternative slots
            if (result["alternativeSlots"] is JArray alternatives && alternatives.Count > 0)
            {
                message += "\n\n💡 *Alternatif Saatler:*\n";
                foreach (var slot in alternatives.Take(3))
                {
                    var altTime = slot["time"]?.ToString();
                    message += $"• {altTime}\n";
                }
            }

            return message;
        }
    }

    /// <summary>
    /// Format error response
    /// </summary>
    public static string FormatErrorResponse(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return IsEnglish()
                ? "❌ Operation failed. Please try again."
                : "❌ İşlem başarısız oldu. Lütfen tekrar deneyin.";
        }

        return IsEnglish()
            ? $"❌ *Error*\n\n{errorMessage}\n\nPlease check the information and try again."
            : $"❌ *Hata*\n\n{errorMessage}\n\nLütfen bilgileri kontrol edip tekrar deneyin.";
    }
}

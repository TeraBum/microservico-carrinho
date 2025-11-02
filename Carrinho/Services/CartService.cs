using System.Text;
using System.Text.Json;

namespace Carrinho.Services;
using Microsoft.EntityFrameworkCore;
using Carrinho.DTO;

public class CartService
{
    private readonly UserDb _userDb;
    private readonly CartDb _cartDb;
    private readonly IHttpClientFactory _httpClientFactory;
    public CartService(UserDb userDb, CartDb cartDb, IHttpClientFactory httpClientFactory)
    {
       _userDb = userDb;
       _cartDb = cartDb;
       _httpClientFactory = httpClientFactory;
    }

    public async Task<Cart?> getCartIfExists(User user)
    {
        var cart = await _cartDb.Cart.Include(c => c.Items).Where(c => c.Status == "Active" && c.UserId == user.Id).FirstOrDefaultAsync();
        if (cart is null)
            return null;
        return cart;
    }
    
    public async Task<User> getUser(string email)
    {
        var user = await _userDb.Users.Where(u => u.Email == email).FirstAsync();
        if (user is null)
            throw new KeyNotFoundException(email);
        return user;
    }

    public async Task<Cart> createCartIfNotExist(CartDto cartDto,  string email)
    {
        var user = await getUser(email);
        var oldCart = await this.getCartIfExists(user);
        if (oldCart is not null)
        {
            throw new InvalidOperationException("Cart already exists");
        }
        var cart = new Cart();
        cart.Id = Guid.NewGuid();
        cart.UserId = user.Id;
        cart.Status = "Active";
        cart.Items = cartDto.Items.Select(i => new CartItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            CartId = cart.Id,
            UnitPrice = i.UnitPrice
        }).ToList();
        _cartDb.Cart.Add(cart);
        await _cartDb.SaveChangesAsync();
        return cart;
    }

    public async Task<Cart> cancelCartIfExist(string email)
    {
        var user = await getUser(email);
        var oldCart = await this.getCartIfExists(user);
        if (oldCart is null)
        {
            throw new InvalidOperationException("No active cart found.");
        }
        oldCart.Status = "Cancelled";
        await _cartDb.SaveChangesAsync();
        return oldCart;
    }

    public async Task<Cart> addItemsToCart(CartItemDto[] cartItems, string email)
    {
        var user = await getUser(email);
        var cart = await getCartIfExists(user);
        if (cart is null)
            throw new KeyNotFoundException("No active cart found.");
        var newCartItems = cartItems.Select(i => new CartItem
        {
            CartId = cart.Id,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            ProductId = i.ProductId,
        }).ToList();
        cart.Items = newCartItems;
        await _cartDb.SaveChangesAsync();
        return cart;
    }

    public async Task<Cart> checkOutCart(string email)
    {
        var user = await getUser(email);
        var oldCart = await this.getCartIfExists(user);
        if (oldCart is null || oldCart.Items is null)
        {
            throw new InvalidOperationException("No active cart found.");
        }
        var client = _httpClientFactory.CreateClient("OrderService");

        var payload = new
        {
            userId = user.Id,
            items = oldCart.Items.Select(i => new {productId = i.ProductId, quantity = i.Quantity, unitPrice = i.UnitPrice}),
        };
        
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        /*
        //var response = await client.PostAsync("/orders", content);
        if (!response.IsSuccessStatusCode)
        {
            //var errorBody = await response.Content.ReadAsStringAsync();
            //throw new InvalidOperationException("It was not possible to create a new order.");
        }
        */
        oldCart.Status = "Finished";
        await _cartDb.SaveChangesAsync();
        return oldCart;
    }
}
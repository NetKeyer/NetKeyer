using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nito.AsyncEx;
using RestSharp;

// IsExternalInit shim removed - now built into .NET 8+

namespace Util
{
    record BigCommerceListResponse<T>(List<T> data, object meta);

    record BigCommerceSingleResponse<T>(T data, object meta);

    record BigCommerceProduct(int Id);

    record BigCommerceCart(string Id);

    record BigCommerceCartUrls(string cart_url, string checkout_url, string embedded_checkout_url);

    record BigCommerceCartRequest(List<BigCommerceLineItem> line_items, string locale);

    record BigCommerceLineItem(int quantity, int product_id, List<BigCommerceOptionSelection> option_selections);

    record BigCommerceModifier(int Id, string display_name);

    record BigCommerceOptionSelection(int option_id, string option_value);

    public class BigCommerce
    {
        private const string STORE_API_URL = "https://www.flexradio.com/wp-json/bc/v3";
        private const string UPGRADE_SKU = "SSDRV3";
        private const string RADIO_ID_MODIFIER = "Radio ID";

        private static BigCommerce? _singleton;
        private RestClient _client = new(STORE_API_URL);

        private BigCommerce()
        {
            UpgradeProductId = new AsyncLazy<int>(async () => await GetProductIdForSku(UPGRADE_SKU));
            RadioIdModifier = new AsyncLazy<int>(async () =>
                await GetProductModifierByDisplayName(await UpgradeProductId, RADIO_ID_MODIFIER));
        }

        public static BigCommerce GetClient()
        {
            if (_singleton != null)
                return _singleton;

            _singleton = new BigCommerce();

            return _singleton;
        }

        private AsyncLazy<int> UpgradeProductId;
        private AsyncLazy<int> RadioIdModifier;

        private async Task<int> GetProductIdForSku(string sku)
        {
            var request = new RestRequest("catalog/products").AddParameter("sku", sku);
            var response = await _client.GetAsync<BigCommerceListResponse<BigCommerceProduct>>(request);
            var productId = response?.data.FirstOrDefault()?.Id ??
                            throw new KeyNotFoundException($"Can't find the SKU {sku}");

            return productId;
        }

        private async Task<int> GetProductModifierByDisplayName(int productId, string variantName)
        {
            var request = new RestRequest("catalog/products/{productId}/modifiers")
                .AddUrlSegment("productId", productId);

            var response = await _client.GetAsync<BigCommerceListResponse<BigCommerceModifier>>(request);
            var modifierId = response.data.FirstOrDefault(m => m.display_name == variantName)?.Id ??
                             throw new KeyNotFoundException("Couldn't find product variant");

            return modifierId;
        }

        private async Task<string> CreateCartWithUpgradeProduct(int productId, int modifierId, string radioId)
        {
            var cart = new BigCommerceCartRequest(new List<BigCommerceLineItem>
            {
                new (1, productId, new List<BigCommerceOptionSelection>
                {
                    new (modifierId, radioId)
                })
            }, "en");

            var request = new RestRequest("carts", Method.Post).AddJsonBody(cart);
            var response = await _client.PostAsync<BigCommerceSingleResponse<BigCommerceCart>>(request);
            var cartId = response?.data?.Id ?? throw new SystemException("Couldn't get a new cart");

            return cartId;
        }

        private async Task<string> GetCartUrl(string cartId)
        {
            var request = new RestRequest("carts/{cartId}/redirect_urls")
                .AddUrlSegment("cartId", cartId);

            var response = await _client.PostAsync<BigCommerceSingleResponse<BigCommerceCartUrls>>(request);
            return response?.data.checkout_url ?? throw new KeyNotFoundException("Couldn't find cart");
        }

        public async Task<string> CreateCart(string radioId)
        {
            var cartId = await CreateCartWithUpgradeProduct(await UpgradeProductId, await RadioIdModifier, radioId);
            return await GetCartUrl(cartId);
        }
    }
}
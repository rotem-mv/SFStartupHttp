﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Http.Utilities;
using System;
using System.Fabric;
using System.Threading.Tasks;

namespace Application1.ValuesService.Controllers
{
    [Route("api/[controller]")]
    public class ValuesController : Controller
    {
        public ValuesController(IReliableStateManager stateManager, ServiceContext serviceContext)
        {
            this.serviceContext = serviceContext;
            this.stateManager = stateManager;
        }

        [HttpGet]
        public async Task<long> GetAsync()
        {
            long count = 0;
            var entities = await this.GetEntitiesAsync();
            await ExponentialBackoff.Run(async () =>
            {
                using (var tx = this.stateManager.CreateTransaction())
                {
                    count = await entities.GetCountAsync(tx);
                }
            });
            return count;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return this.BadRequest();
            }

            ValuesEntity s = await this.GetEntityAsync(id);
            if (s == null)
            {
                return this.NotFound();
            }

            return new JsonResult(s);
        }
        
        [HttpPost("{id}")]
        public async Task<IActionResult> PostAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return this.BadRequest();
            }

            ValuesEntity s = await this.CreateEntityAsync(id);
            return new JsonResult(s);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutAsync(string id, [FromBody]ValuesEntity value)
        {
            if (string.IsNullOrWhiteSpace(id) || value == null)
            {
                return this.BadRequest();
            }

            await this.UpdateEntityAsync(id, value);
            return this.Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return this.BadRequest();
            }

            await this.DeleteEntityAsync(id);
            return this.Ok();
        }

        #region private members
        private async Task<ValuesEntity> GetEntityAsync(string id)
        {
            ValuesEntity entity = null;
            var entities = await this.GetEntitiesAsync();
            await ExponentialBackoff.Run(async () =>
            {
                using (var tx = this.stateManager.CreateTransaction())
                {
                    var v = await entities.TryGetValueAsync(tx, id);
                    if (v.HasValue)
                    {
                        entity = new ValuesEntity(v.Value.Id, v.Value.CreatedOn, v.Value.LastModifiedOn, DateTimeOffset.UtcNow, v.Value.Values);
                        await entities.SetAsync(tx, id, entity);
                    }
                    await tx.CommitAsync();
                }
            });
            return entity;
        }

        private async Task<ValuesEntity> CreateEntityAsync(string id)
        {
            ValuesEntity entity = null;
            var entities = await this.GetEntitiesAsync();
            await ExponentialBackoff.Run(async () =>
            {
                entity = new ValuesEntity(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                using (var tx = this.stateManager.CreateTransaction())
                {
                    await entities.AddAsync(tx, id, entity);
                    await tx.CommitAsync();
                }
            });
            return entity;
        }

        private async Task UpdateEntityAsync(string id, ValuesEntity data)
        {
            var entities = await this.GetEntitiesAsync();
            await ExponentialBackoff.Run(async () =>
            {
                using (var tx = this.stateManager.CreateTransaction())
                {
                    var v = await entities.TryGetValueAsync(tx, id);
                    ValuesEntity entity = v.HasValue ? new ValuesEntity(id, v.Value.CreatedOn, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, data.Values)
                                                     : new ValuesEntity(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTime.UtcNow, data.Values);
                    await entities.SetAsync(tx, id, entity);
                    await tx.CommitAsync();
                }
            });
        }

        private async Task DeleteEntityAsync(string id)
        {
            var entities = await this.GetEntitiesAsync();
            await ExponentialBackoff.Run(async () =>
            {
                using (var tx = this.stateManager.CreateTransaction())
                {
                    await entities.TryRemoveAsync(tx, id);
                    await tx.CommitAsync();
                }
            });
        }
        
        private Task<IReliableDictionary<string, ValuesEntity>> GetEntitiesAsync()
        {
            return this.stateManager.GetOrAddAsync<IReliableDictionary<string, ValuesEntity>>("Values");
        }

        private readonly IReliableStateManager stateManager;
        private readonly ServiceContext serviceContext;
        #endregion
    }
}

﻿using FluentResults;
using MediatR;

namespace NadinSoft.Application.Abstractions.Messaging
{
    public interface ICommand<TResponse> : IRequest<Result<TResponse>>
    {
    }
}
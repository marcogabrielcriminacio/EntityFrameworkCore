﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Expressions.Internal;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal.NavigationExpansion
{
    public class NavigationPropertyBindingExpressionVisitor : NavigationExpansionExpressionVisitorBase
    {
        private ParameterExpression _rootParameter;
        private List<SourceMapping> _sourceMappings;

        public NavigationPropertyBindingExpressionVisitor(
            ParameterExpression rootParameter,
            List<SourceMapping> sourceMappings)
        {
            _rootParameter = rootParameter;
            _sourceMappings = sourceMappings;
        }

        private (ParameterExpression rootParameter, List<INavigation> navigations) TryFindMatchingTransparentIdentifierMapping(
            Expression expression,
            List<string> initialPath,
            List<(List<string> path, List<INavigation> navigations)> transparentIdentifierMappingCandidates)
        {
            if (expression is ParameterExpression parameterExpression
                && (parameterExpression == _rootParameter || _rootParameter == null)
                && initialPath.Count == 0)
            {
                var matchingCandidate = transparentIdentifierMappingCandidates.Where(m => m.path.Count == 0).SingleOrDefault();

                return matchingCandidate.navigations != null
                    ? (rootParameter: parameterExpression, matchingCandidate.navigations)
                    : (null, null);
            }

            if (expression is MemberExpression memberExpression)
            {
                var matchingCandidates = transparentIdentifierMappingCandidates.Where(m => m.path.Count > 0 && m.path.Last() == memberExpression.Member.Name);
                var newCandidates = matchingCandidates.Select(mc => (path: mc.path.Take(mc.path.Count - 1).ToList(), mc.navigations.ToList())).ToList();
                if (newCandidates.Any())
                {
                    var result = TryFindMatchingTransparentIdentifierMapping(memberExpression.Expression, initialPath, newCandidates);
                    if (result.rootParameter != null)
                    {
                        return result;
                    }
                }

                if (initialPath.Count > 0 && memberExpression.Member.Name == initialPath.Last())
                {
                    var emptyCandidates = transparentIdentifierMappingCandidates.Where(m => m.path.Count == 0).ToList();
                    if (emptyCandidates.Count > 0)
                    {
                        return TryFindMatchingTransparentIdentifierMapping(memberExpression.Expression, initialPath.Take(initialPath.Count - 1).ToList(), emptyCandidates);
                    }
                }
            }

            return (null, null);
        }

        //protected override Expression VisitExtension(Expression extensionExpression)
        //{
        //    if (extensionExpression is NavigationExpansionExpression navigationExpansionExpression)
        //    {
        //        var newOperand = Visit(navigationExpansionExpression.Operand);
        //        if (newOperand != navigationExpansionExpression.Operand)
        //        {
        //            return new NavigationExpansionExpression(
        //                newOperand,
        //                navigationExpansionExpression.State,
        //                navigationExpansionExpression.Type);
        //        }
        //    }

        //    if (extensionExpression is NullSafeEqualExpression nullSafeEqualExpression)
        //    {
        //        var newOuterKeyNullCheck = Visit(nullSafeEqualExpression.OuterKeyNullCheck);
        //        var newEqualExpression = (BinaryExpression)Visit(nullSafeEqualExpression.EqualExpression);

        //        if (newOuterKeyNullCheck != nullSafeEqualExpression.OuterKeyNullCheck
        //            || newEqualExpression != nullSafeEqualExpression.EqualExpression)
        //        {
        //            return new NullSafeEqualExpression(newOuterKeyNullCheck, newEqualExpression);
        //        }
        //    }

        //    return extensionExpression;
        //}

        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            var newExpression = Visit(memberExpression.Expression);
            var boundProperty = TryBindProperty(memberExpression, newExpression, memberExpression.Member.Name);

            return boundProperty ?? memberExpression.Update(newExpression);
        }


        //protected override Expression VisitMember(MemberExpression memberExpression)
        //{
        //    var newExpression = Visit(memberExpression.Expression);
        //    if (newExpression is NavigationBindingExpression navigationBindingExpression)
        //    {
        //        if (navigationBindingExpression.RootParameter == _rootParameter)
        //        {
        //            var navigation = navigationBindingExpression.EntityType.FindNavigation(memberExpression.Member.Name);
        //            if (navigation != null)
        //            {
        //                var navigations = navigationBindingExpression.Navigations.ToList();
        //                navigations.Add(navigation);

        //                return new NavigationBindingExpression(
        //                    memberExpression,
        //                    navigationBindingExpression.RootParameter,
        //                    navigations,
        //                    navigation.GetTargetType(),
        //                    navigationBindingExpression.SourceMapping);
        //            }
        //        }
        //    }
        //    else
        //    {
        //        foreach (var sourceMapping in _sourceMappings)
        //        {
        //            var match = TryFindMatchingTransparentIdentifierMapping(memberExpression, sourceMapping.InitialPath, sourceMapping.TransparentIdentifierMapping);
        //            if (match.rootParameter != null)
        //            {
        //                return new NavigationBindingExpression(
        //                    memberExpression,
        //                    match.rootParameter,
        //                    match.navigations,
        //                    match.navigations.Count > 0 ? match.navigations.Last().GetTargetType() : sourceMapping.RootEntityType,
        //                    sourceMapping);
        //            }
        //        }
        //    }

        //    return memberExpression.Update(newExpression);
        //}

        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            if (methodCallExpression.Method.IsEFPropertyMethod())
            {
                var newCaller = Visit(methodCallExpression.Arguments[0]);
                var propertyName = (string)((ConstantExpression)methodCallExpression.Arguments[1]).Value;

                var boundProperty = TryBindProperty(methodCallExpression, newCaller, propertyName);

                return boundProperty ?? methodCallExpression.Update(methodCallExpression.Object, new[] { newCaller, methodCallExpression.Arguments[1] });
            }

            return base.VisitMethodCall(methodCallExpression);
        }

        private Expression TryBindProperty(Expression originalExpression, Expression newExpression, string navigationMemberName)
        {
            if (newExpression is NavigationBindingExpression navigationBindingExpression)
            {
                if (navigationBindingExpression.RootParameter == _rootParameter)
                {
                    var navigation = navigationBindingExpression.EntityType.FindNavigation(navigationMemberName);
                    if (navigation != null)
                    {
                        var navigations = navigationBindingExpression.Navigations.ToList();
                        navigations.Add(navigation);

                        return new NavigationBindingExpression(
                            originalExpression,
                            navigationBindingExpression.RootParameter,
                            navigations,
                            navigation.GetTargetType(),
                            navigationBindingExpression.SourceMapping);
                    }
                }
            }
            else
            {
                foreach (var sourceMapping in _sourceMappings)
                {
                    var match = TryFindMatchingTransparentIdentifierMapping(originalExpression, sourceMapping.InitialPath, sourceMapping.TransparentIdentifierMapping);
                    if (match.rootParameter != null)
                    {
                        return new NavigationBindingExpression(
                            originalExpression,
                            match.rootParameter,
                            match.navigations,
                            match.navigations.Count > 0 ? match.navigations.Last().GetTargetType() : sourceMapping.RootEntityType,
                            sourceMapping);
                    }
                }
            }

            return null;
        }

        protected override Expression VisitLambda<T>(Expression<T> lambdaExpression)
        {
            var newBody = Visit(lambdaExpression.Body);

            return newBody != lambdaExpression.Body
                ? Expression.Lambda(newBody, lambdaExpression.Parameters)
                : lambdaExpression;
        }

        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            if (parameterExpression == _rootParameter
                || _rootParameter == null)
            {
                var sourceMapping = _sourceMappings.Where(sm => sm.RootEntityType.ClrType == parameterExpression.Type && sm.InitialPath.Count == 0).SingleOrDefault();
                if (sourceMapping != null)
                {
                    return new NavigationBindingExpression(
                        parameterExpression,
                        parameterExpression,
                        new List<INavigation>(),
                        sourceMapping.RootEntityType,
                        sourceMapping);
                }
            }

            return parameterExpression;
        }
    }

    public class NavigationPropertyBindingExpressionVisitor2 : NavigationExpansionExpressionVisitorBase
    {
        private ParameterExpression _rootParameter;
        private List<SourceMapping2> _sourceMappings;

        public NavigationPropertyBindingExpressionVisitor2(
            ParameterExpression rootParameter,
            List<SourceMapping2> sourceMappings)
        {
            _rootParameter = rootParameter;
            _sourceMappings = sourceMappings;
        }

        protected override Expression VisitExtension(Expression extensionExpression)
        {
            if (extensionExpression is NavigationBindingExpression2 navigationBindingExpression)
            {
                return navigationBindingExpression;
            }

            return base.VisitExtension(extensionExpression);
        }

        protected override Expression VisitLambda<T>(Expression<T> lambdaExpression)
        {
            var newBody = Visit(lambdaExpression.Body);

            return newBody != lambdaExpression.Body
                ? Expression.Lambda(newBody, lambdaExpression.Parameters)
                : lambdaExpression;
        }

        protected override Expression VisitParameter(ParameterExpression parameterExpression)
        {
            if (parameterExpression == _rootParameter)
            {
                // TODO: is this wrong? Accessible root could be pushed further into the navigation tree using projections
                var sourceMapping = _sourceMappings.Where(sm => sm.RootEntityType.ClrType == parameterExpression.Type && sm.NavigationTree.FromMappings.Any(fm => fm.Count == 0)).SingleOrDefault();
                if (sourceMapping != null)
                {
                    return new NavigationBindingExpression2(
                        parameterExpression,
                        sourceMapping.NavigationTree,
                        sourceMapping.RootEntityType,
                        sourceMapping,
                        parameterExpression.Type);
                }
            }

            return parameterExpression;
        }

        protected override Expression VisitMember(MemberExpression memberExpression)
        {
            var newExpression = Visit(memberExpression.Expression);
            var boundProperty = TryBindProperty(memberExpression, newExpression, memberExpression.Member.Name);

            return boundProperty ?? memberExpression.Update(newExpression);
        }

        protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
        {
            if (methodCallExpression.Method.IsEFPropertyMethod())
            {
                var newCaller = Visit(methodCallExpression.Arguments[0]);
                var propertyName = (string)((ConstantExpression)methodCallExpression.Arguments[1]).Value;

                var boundProperty = TryBindProperty(methodCallExpression, newCaller, propertyName);

                return boundProperty ?? methodCallExpression.Update(methodCallExpression.Object, new[] { newCaller, methodCallExpression.Arguments[1] });
            }

            return base.VisitMethodCall(methodCallExpression);
        }

        private Expression TryBindProperty(Expression originalExpression, Expression newExpression, string navigationMemberName)
        {
            if (newExpression is NavigationBindingExpression2 navigationBindingExpression)
            {
                if (navigationBindingExpression.RootParameter == _rootParameter)
                {
                    var navigation = navigationBindingExpression.EntityType.FindNavigation(navigationMemberName);
                    if (navigation != null)
                    {
                        var navigationTreeNode = NavigationTreeNode2.Create(navigationBindingExpression.SourceMapping, navigation, navigationBindingExpression.NavigationTreeNode);

                        // TODO: is original expression still needed now?!
                        return new NavigationBindingExpression2(
                            navigationBindingExpression.RootParameter,
                            navigationTreeNode,
                            navigation.GetTargetType(),
                            navigationBindingExpression.SourceMapping,
                            originalExpression.Type);
                    }

                }
            }
            else
            {
                foreach (var sourceMapping in _sourceMappings)
                {
                    var candidates = sourceMapping.NavigationTree.Flatten().SelectMany(n => n.FromMappings, (n, m) => (navigationTreeNode: n, path: m)).ToList();
                    var match = TryFindMatchingNavigationTreeNode(originalExpression, candidates);
                    if (match.navigationTreeNode != null)
                    {
                        return new NavigationBindingExpression2(
                            match.rootParameter,
                            match.navigationTreeNode,
                            match.navigationTreeNode.Navigation?.GetTargetType() ?? sourceMapping.RootEntityType,
                            // TODO: currently not matching root, navigation could be null!
                            //match.navigations.Count > 0 ? match.navigations.Last().GetTargetType() : sourceMapping.RootEntityType,
                            sourceMapping,
                            originalExpression.Type);
                    }
                }
            }

            return null;
        }

        private (ParameterExpression rootParameter, NavigationTreeNode2 navigationTreeNode) TryFindMatchingNavigationTreeNode(
            Expression expression,
            List<(NavigationTreeNode2 navigationTreeNode, List<string> path)> navigationTreeNodeCandidates)
        {
            if (expression is ParameterExpression parameterExpression
                && (parameterExpression == _rootParameter/* || _rootParameter == null*/))
            {
                var matchingCandidate = navigationTreeNodeCandidates.Where(m => m.path.Count == 0).SingleOrDefault();

                return matchingCandidate.navigationTreeNode != null
                    ? (rootParameter: parameterExpression, matchingCandidate.navigationTreeNode)
                    : (null, null);
            }

            if (expression is MemberExpression memberExpression)
            {
                var matchingCandidates = navigationTreeNodeCandidates.Where(m => m.path.Count > 0 && m.path.Last() == memberExpression.Member.Name);
                var newCandidates = matchingCandidates.Select(mc => (mc.navigationTreeNode, path: mc.path.Take(mc.path.Count - 1).ToList())).ToList();
                if (newCandidates.Any())
                {
                    var result = TryFindMatchingNavigationTreeNode(memberExpression.Expression, newCandidates);
                    if (result.rootParameter != null)
                    {
                        return result;
                    }
                }

                // TODO: match with root
                //if (initialPath.Count > 0 && memberExpression.Member.Name == initialPath.Last())
                //{
                //    var emptyCandidates = transparentIdentifierMappingCandidates.Where(m => m.path.Count == 0).ToList();
                //    if (emptyCandidates.Count > 0)
                //    {
                //        return TryFindMatchingTransparentIdentifierMapping(memberExpression.Expression, initialPath.Take(initialPath.Count - 1).ToList(), emptyCandidates);
                //    }
                //}
            }

            return (null, null);
        }
    }



    public class NavigationPropertyUnbindingBindingExpressionVisitor2 : NavigationExpansionExpressionVisitorBase
    {
        private ParameterExpression _rootParameter;

        public NavigationPropertyUnbindingBindingExpressionVisitor2(ParameterExpression rootParameter)
        {
            _rootParameter = rootParameter;
        }

        protected override Expression VisitExtension(Expression extensionExpression)
        {
            if (extensionExpression is NavigationBindingExpression2 navigationBindingExpression
                && navigationBindingExpression.RootParameter == _rootParameter)
            {
                return navigationBindingExpression.NavigationTreeNode.BuildExpression(navigationBindingExpression.RootParameter);
            }

            return base.VisitExtension(extensionExpression);
        }
    }




    //public class NavigationPropertyReverseBindingExpressionVisitor2 : NavigationExpansionExpressionVisitorBase
    //{
    //    private ParameterExpression _rootParameter;
    //    private List<SourceMapping2> _sourceMappings;

    //    public NavigationPropertyReverseBindingExpressionVisitor2(
    //        ParameterExpression rootParameter,
    //        List<SourceMapping2> sourceMappings)
    //    {
    //        _rootParameter = rootParameter;
    //        _sourceMappings = sourceMappings;
    //    }

    //    protected override Expression VisitLambda<T>(Expression<T> lambdaExpression)
    //    {
    //        var newBody = Visit(lambdaExpression.Body);

    //        return newBody != lambdaExpression.Body
    //            ? Expression.Lambda(newBody, lambdaExpression.Parameters)
    //            : lambdaExpression;
    //    }

    //    protected override Expression VisitParameter(ParameterExpression parameterExpression)
    //    {
    //        if (parameterExpression == _rootParameter)
    //        {
    //            // TODO: is this wrong? Accessible root could be pushed further into the navigation tree using projections
    //            var sourceMapping = _sourceMappings.Where(sm => sm.RootEntityType.ClrType == parameterExpression.Type && sm.NavigationTree.ToMapping.Count == 0).SingleOrDefault();
    //            if (sourceMapping != null)
    //            {
    //                return new NavigationBindingExpression2(
    //                    parameterExpression,
    //                    parameterExpression,
    //                    sourceMapping.NavigationTree,
    //                    sourceMapping.RootEntityType,
    //                    sourceMapping);
    //            }
    //        }

    //        return parameterExpression;
    //    }

    //    protected override Expression VisitMember(MemberExpression memberExpression)
    //    {
    //        var newExpression = Visit(memberExpression.Expression);
    //        var boundProperty = TryBindProperty(memberExpression, newExpression, memberExpression.Member.Name);

    //        return boundProperty ?? memberExpression.Update(newExpression);
    //    }

    //    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    //    {
    //        if (methodCallExpression.Method.IsEFPropertyMethod())
    //        {
    //            var newCaller = Visit(methodCallExpression.Arguments[0]);
    //            var propertyName = (string)((ConstantExpression)methodCallExpression.Arguments[1]).Value;

    //            var boundProperty = TryBindProperty(methodCallExpression, newCaller, propertyName);

    //            return boundProperty ?? methodCallExpression.Update(methodCallExpression.Object, new[] { newCaller, methodCallExpression.Arguments[1] });
    //        }

    //        return base.VisitMethodCall(methodCallExpression);
    //    }

    //    private Expression TryBindProperty(Expression originalExpression, Expression newExpression, string navigationMemberName)
    //    {
    //        if (newExpression is NavigationBindingExpression2 navigationBindingExpression)
    //        {
    //            if (navigationBindingExpression.RootParameter == _rootParameter)
    //            {
    //                var navigation = navigationBindingExpression.EntityType.FindNavigation(navigationMemberName);
    //                if (navigation != null)
    //                {
    //                    var navigationTreeNode = NavigationTreeNode2.Create(navigationBindingExpression.SourceMapping, navigation, navigationBindingExpression.NavigationTreeNode);

    //                    return new NavigationBindingExpression2(
    //                        originalExpression,
    //                        navigationBindingExpression.RootParameter,
    //                        navigationTreeNode,
    //                        navigation.GetTargetType(),
    //                        navigationBindingExpression.SourceMapping);
    //                }

    //            }
    //        }
    //        else
    //        {
    //            foreach (var sourceMapping in _sourceMappings)
    //            {
    //                var candidates = sourceMapping.NavigationTree.Flatten().Select(n => (navigationTreeNode: n, path: n.ToMapping)).ToList();
    //                var match = TryFindMatchingNavigationTreeNode(originalExpression, candidates);
    //                if (match.navigationTreeNode != null)
    //                {
    //                    return new NavigationBindingExpression2(
    //                        originalExpression,
    //                        match.rootParameter,
    //                        match.navigationTreeNode,
    //                        match.navigationTreeNode.Navigation?.GetTargetType() ?? sourceMapping.RootEntityType,
    //                        // TODO: currently not matching root, navigation could be null!
    //                        //match.navigations.Count > 0 ? match.navigations.Last().GetTargetType() : sourceMapping.RootEntityType,
    //                        sourceMapping);
    //                }
    //            }
    //        }

    //        return null;
    //    }

    //    private (ParameterExpression rootParameter, NavigationTreeNode2 navigationTreeNode) TryFindMatchingNavigationTreeNode(
    //        Expression expression,
    //        List<(NavigationTreeNode2 navigationTreeNode, List<string> path)> navigationTreeNodeCandidates)
    //    {
    //        if (expression is ParameterExpression parameterExpression
    //            && (parameterExpression == _rootParameter))
    //        {
    //            var matchingCandidate = navigationTreeNodeCandidates.Where(m => m.path.Count == 0).SingleOrDefault();

    //            return matchingCandidate.navigationTreeNode != null
    //                ? (rootParameter: parameterExpression, matchingCandidate.navigationTreeNode)
    //                : (null, null);
    //        }

    //        if (expression is MemberExpression memberExpression)
    //        {
    //            var matchingCandidates = navigationTreeNodeCandidates.Where(m => m.path.Count > 0 && m.path.Last() == memberExpression.Member.Name);
    //            var newCandidates = matchingCandidates.Select(mc => (mc.navigationTreeNode, path: mc.path.Take(mc.path.Count - 1).ToList())).ToList();
    //            if (newCandidates.Any())
    //            {
    //                var result = TryFindMatchingNavigationTreeNode(memberExpression.Expression, newCandidates);
    //                if (result.rootParameter != null)
    //                {
    //                    return result;
    //                }
    //            }
    //        }

    //        return (null, null);
    //    }
    //}
}